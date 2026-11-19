import 'dart:async';
import 'dart:io';
import 'dart:math';

import 'package:audio_service/audio_service.dart';
import 'package:audio_session/audio_session.dart';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:just_audio/just_audio.dart';
import 'package:path_provider/path_provider.dart';
import 'package:sqflite/sqflite.dart';

import '../../core/auth/session.dart';
import '../../shared/models/library_models.dart';

class LocalTrack {
  const LocalTrack(this.id, this.title, this.path, {this.artist, this.artUrl});
  final String id, title, path;
  final String? artist, artUrl;
}

final localLibraryProvider = FutureProvider<LocalLibrary>((ref) async {
  final owner = ref.watch(sessionProvider).value?.userId ?? 'guest';
  return LocalLibrary.open(owner);
}, retry: (_, _) => null);

class LocalLibrary {
  LocalLibrary(this.directory, this.db);
  final Directory? directory;
  final Database? db;

  static Future<LocalLibrary> open(String owner) async {
    if (Platform.environment.containsKey('FLUTTER_TEST')) {
      return LocalLibrary(null, null);
    }
    final root = await getApplicationSupportDirectory();
    final safe = owner.replaceAll(RegExp('[^a-zA-Z0-9-]'), '');
    final dir = await Directory('${root.path}/audio/$safe')
        .create(recursive: true);
    final db = await openDatabase(
      '${dir.path}/index.db',
      version: 2,
      onCreate: (d, _) async {
        await d.execute(
          'CREATE TABLE tracks (id TEXT PRIMARY KEY,title TEXT NOT NULL,path TEXT NOT NULL,created INTEGER NOT NULL)',
        );
        await d.execute(
          'CREATE TABLE album_tracks (albumId TEXT NOT NULL,trackId TEXT NOT NULL,position INTEGER NOT NULL,PRIMARY KEY(albumId,trackId))',
        );
        await d.execute(
          'CREATE INDEX idx_album_tracks_order ON album_tracks(albumId,position)',
        );
      },
      onUpgrade: (d, oldVersion, _) async {
        if (oldVersion < 2) {
          await d.execute(
            'CREATE TABLE album_tracks (albumId TEXT NOT NULL,trackId TEXT NOT NULL,position INTEGER NOT NULL,PRIMARY KEY(albumId,trackId))',
          );
          await d.execute(
            'CREATE INDEX idx_album_tracks_order ON album_tracks(albumId,position)',
          );
        }
      },
    );
    return LocalLibrary(dir, db);
  }

  File? _songFile(String songId) {
    final dir = directory;
    if (dir == null) return null;
    final clean = songId.replaceAll(RegExp('[^a-zA-Z0-9-]'), '');
    return File('${dir.path}/song_$clean.mp3');
  }

  Future<bool> hasDownloadedSong(String songId) async {
    final f = _songFile(songId);
    return f != null && await f.exists() && await f.length() > 16;
  }

  Future<LocalTrack?> getDownloadedSongTrack(Song song) async {
    final f = _songFile(song.id);
    if (f == null || !await f.exists() || await f.length() <= 16) return null;
    return LocalTrack(
      'song:${song.id}',
      song.title,
      f.path,
      artist: song.artist ?? song.originalCreator,
      artUrl: song.thumbnail,
    );
  }

  Future<LocalTrack> saveDownloadedSongMp3(Song song, Uint8List bytes) async {
    final dir = directory;
    final database = db;
    if (dir == null || database == null) {
      throw const FormatException(
        'Bộ nhớ cục bộ không khả dụng trong môi trường kiểm thử.',
      );
    }
    if (bytes.length <= 16) {
      throw const FormatException('Dữ liệu MP3 tải về không hợp lệ.');
    }
    final out = _songFile(song.id)!;
    await out.writeAsBytes(bytes, flush: true);
    return _indexSongTrack(database, song, out);
  }

  Future<void> copyDownloadedSongMp3(Song song, LocalTrack source) async {
    final out = _songFile(song.id);
    final database = db;
    if (out == null || database == null) {
      throw const FileSystemException('Local MP3 storage unavailable');
    }
    if (!await hasDownloadedSong(song.id)) {
      final temp = await File(source.path).copy('${out.path}.sync');
      await temp.rename(out.path);
    }
    await _indexSongTrack(database, song, out);
  }

  /// Copies an imported device file to another on-device owner without uploading it.
  Future<void> copyImportedTrack(LocalTrack source) async {
    final dir = directory;
    final database = db;
    if (dir == null || database == null) {
      throw const FileSystemException('Local audio storage unavailable');
    }
    final id = source.id.replaceAll(RegExp('[^a-zA-Z0-9-]'), '');
    final extension = source.path.split('.').last.toLowerCase();
    if (id.isEmpty || !['mp3', 'm4a', 'flac'].contains(extension)) {
      throw const FormatException('Unsupported local audio file');
    }
    final sourceFile = File(source.path);
    final size = await sourceFile.length();
    if (size == 0 || size > 500 * 1024 * 1024) {
      throw const FormatException('Invalid local audio file size');
    }
    final out = File('${dir.path}/import_$id.$extension');
    if (!await out.exists() || await out.length() != size) {
      final temp = await sourceFile.copy('${out.path}.claim');
      await temp.rename(out.path);
    }
    await database.insert('tracks', {
      'id': source.id,
      'title': source.title,
      'path': out.path,
      'created': DateTime.now().toUtc().millisecondsSinceEpoch,
    }, conflictAlgorithm: ConflictAlgorithm.replace);
  }

  Future<LocalTrack> _indexSongTrack(
    Database database,
    Song song,
    File out,
  ) async {
    final trackId = 'song:${song.id}';
    await database.insert('tracks', {
      'id': trackId,
      'title': song.title,
      'path': out.path,
      'created': DateTime.now().toUtc().millisecondsSinceEpoch,
    }, conflictAlgorithm: ConflictAlgorithm.replace);
    return LocalTrack(
      trackId,
      song.title,
      out.path,
      artist: song.artist ?? song.originalCreator,
      artUrl: song.thumbnail,
    );
  }

  Future<LocalTrack> ensureSongTrack(
    Song song,
    Future<Uint8List> Function() fetcher,
  ) async {
    final existing = await getDownloadedSongTrack(song);
    if (existing != null) return existing;
    final bytes = await fetcher();
    return saveDownloadedSongMp3(song, bytes);
  }

  Future<void> removeSongTrack(String songId) async {
    final f = _songFile(songId);
    if (f != null && await f.exists()) {
      await f.delete();
    }
    await db?.delete('tracks', where: 'id=?', whereArgs: ['song:$songId']);
  }

  Future<List<LocalTrack>> list() async {
    final database = db;
    if (database == null) return const [];
    return (await database.query('tracks', orderBy: 'created DESC'))
        .map(
          (j) => LocalTrack(
            j['id'] as String,
            j['title'] as String,
            j['path'] as String,
          ),
        )
        .toList();
  }

  Future<List<LocalTrack>> listAlbumTracks(String albumId) async {
    final database = db;
    if (database == null) return const [];
    final rows = await database.rawQuery(
      'SELECT tracks.id,tracks.title,tracks.path FROM album_tracks JOIN tracks ON tracks.id=album_tracks.trackId WHERE album_tracks.albumId=? ORDER BY album_tracks.position,tracks.id',
      [albumId],
    );
    return rows
        .map(
          (row) => LocalTrack(
            row['id'] as String,
            row['title'] as String,
            row['path'] as String,
          ),
        )
        .toList();
  }

  Future<void> addTrackToAlbum(String trackId, String albumId) async {
    final database = db;
    if (database == null) {
      throw const FileSystemException('Local audio storage unavailable');
    }
    final present = await database.query(
      'album_tracks',
      columns: ['trackId'],
      where: 'albumId=? AND trackId=?',
      whereArgs: [albumId, trackId],
      limit: 1,
    );
    if (present.isNotEmpty) return;
    final maximum = await database.rawQuery(
      'SELECT MAX(position) AS position FROM album_tracks WHERE albumId=?',
      [albumId],
    );
    final position = (maximum.first['position'] as int?) ?? -1;
    await database.insert('album_tracks', {
      'albumId': albumId,
      'trackId': trackId,
      'position': position + 1,
    });
  }

  Future<void> removeTrackFromAlbum(String trackId, String albumId) async {
    await db?.delete(
      'album_tracks',
      where: 'albumId=? AND trackId=?',
      whereArgs: [albumId, trackId],
    );
  }

  Future<void> removeAlbumTracks(String albumId) async {
    await db?.delete('album_tracks', where: 'albumId=?', whereArgs: [albumId]);
  }

  Future<void> reorderAlbumTracks(String albumId, List<String> trackIds) async {
    final database = db;
    if (database == null) return;
    await database.transaction((transaction) async {
      for (final (position, trackId) in trackIds.indexed) {
        await transaction.update(
          'album_tracks',
          {'position': position},
          where: 'albumId=? AND trackId=?',
          whereArgs: [albumId, trackId],
        );
      }
    });
  }

  Future<void> importFile(XFile file) async {
    final dir = directory;
    final database = db;
    if (dir == null || database == null) return;
    final ext = file.name.split('.').last.toLowerCase();
    if (!['mp3', 'm4a', 'flac'].contains(ext) ||
        await file.length() > 500 * 1024 * 1024) {
      throw const FormatException('Chọn MP3, M4A hoặc FLAC không quá 500 MB.');
    }
    final id = List.generate(
      16,
      (_) => Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
    ).join();
    final out = File('${dir.path}/$id.$ext');
    try {
      final sink = out.openWrite();
      var size = 0;
      try {
        await for (final chunk in file.openRead()) {
          size += chunk.length;
          if (size > 500 * 1024 * 1024) {
            throw const FormatException('File vượt quá 500 MB.');
          }
          sink.add(chunk);
        }
        if (size == 0) throw const FormatException('File trống.');
      } finally {
        await sink.close();
      }
      await database.insert('tracks', {
        'id': id,
        'title': file.name,
        'path': out.path,
        'created': DateTime.now().toUtc().millisecondsSinceEpoch,
      });
    } catch (_) {
      if (await out.exists()) await out.delete();
      rethrow;
    }
  }

  Future<void> remove(LocalTrack track) async {
    final file = File(track.path);
    if (await file.exists()) await file.delete();
    await db?.delete('album_tracks', where: 'trackId=?', whereArgs: [track.id]);
    await db?.delete('tracks', where: 'id=?', whereArgs: [track.id]);
  }

  /// Calculates actual disk bytes occupied by user-managed audio files (excludes SQLite index).
  Future<int> storageUsageBytes() async {
    final dir = directory;
    if (dir == null || !await dir.exists()) return 0;
    var total = 0;
    try {
      await for (final entity in dir.list(followLinks: false)) {
        if (entity is! File) continue;
        final name = entity.uri.pathSegments.isEmpty
            ? ''
            : entity.uri.pathSegments.last.toLowerCase();
        if (name.startsWith('index.db')) continue;
        total += await entity.length();
      }
    } catch (_) {}
    return total;
  }
}

final localAudioProvider = FutureProvider<LocalAudio>((ref) async {
  final session = await AudioSession.instance;
  await session.configure(const AudioSessionConfiguration.music());
  final player = await AudioService.init(
    builder: LocalAudio.new,
    config: const AudioServiceConfig(
      androidNotificationChannelId: 'com.tmusic.tmusic.audio',
      androidNotificationChannelName: 'Nhạc trên thiết bị',
      androidNotificationOngoing: true,
    ),
  );
  ref.listen(sessionProvider, (prev, next) {
    if (prev?.value != null && next.value == null) player.stop();
  });
  return player;
}, retry: (_, _) => null);

class LocalAudio extends BaseAudioHandler with QueueHandler, SeekHandler {
  LocalAudio() : _player = AudioPlayer() {
    _bindPlayer(_player!);
  }

  @visibleForTesting
  LocalAudio.forTest({
    Duration initialPosition = Duration.zero,
    Duration? initialDuration,
    List<MediaItem> initialQueue = const [],
    int initialIndex = 0,
    bool playing = true,
  }) : _player = null,
       _testPosition = initialPosition,
       _testDuration = initialDuration {
    if (initialQueue.isNotEmpty) {
      queue.add(initialQueue);
      final clamped = initialIndex.clamp(0, initialQueue.length - 1);
      mediaItem.add(initialQueue[clamped]);
      playbackState.add(
        PlaybackState(
          playing: playing,
          processingState: AudioProcessingState.ready,
          updatePosition: initialPosition,
          queueIndex: clamped,
          repeatMode: AudioServiceRepeatMode.none,
          shuffleMode: AudioServiceShuffleMode.none,
        ),
      );
    }
  }

  void _bindPlayer(AudioPlayer p) {
    p.playbackEventStream.listen(
      (event) => _broadcast(),
      onError: (Object e, StackTrace s) => _failed(),
    );
    p.errorStream.listen((_) => _failed());
    p.currentIndexStream.listen((index) {
      if (index != null && index < queue.value.length) {
        final nextItem = queue.value[index];
        if (mediaItem.value?.id != nextItem.id) {
          abLoop.value = null;
        }
        mediaItem.add(nextItem);
      }
    });
    p.positionStream.listen((pos) {
      final range = abLoop.value;
      if (range != null &&
          !_seekingAb &&
          range.end > range.start &&
          pos >= range.end) {
        _seekingAb = true;
        p.seek(range.start).whenComplete(() => _seekingAb = false);
      }
    });
  }

  final AudioPlayer? _player;
  final StreamController<Duration> _testPosCtrl =
      StreamController<Duration>.broadcast();
  final StreamController<Duration?> _testDurCtrl =
      StreamController<Duration?>.broadcast();
  Duration _testPosition = Duration.zero;
  Duration? _testDuration;
  final ValueNotifier<({Duration start, Duration end})?> abLoop = ValueNotifier(
    null,
  );
  final ValueNotifier<double> speed = ValueNotifier(1.0);
  bool _seekingAb = false;

  Stream<Duration> get position =>
      _player?.positionStream ?? _testPosCtrl.stream;
  Stream<Duration?> get duration =>
      _player?.durationStream ?? _testDurCtrl.stream;
  Duration get currentPosition => _player?.position ?? _testPosition;
  Duration? get currentDuration => _player?.duration ?? _testDuration;

  Future<void> start(
    List<LocalTrack> tracks,
    int index, {
    String? albumName,
  }) async {
    if (!await File(tracks[index].path).exists()) {
      throw const FormatException('File không còn tồn tại. Hãy thêm lại.');
    }
    for (final t in tracks) {
      if (!File(t.path).isAbsolute) {
        throw const FormatException('Đường dẫn không hợp lệ.');
      }
    }
    abLoop.value = null;
    final items = tracks
        .map((t) => _toMediaItem(t, albumName: albumName))
        .toList();
    queue.add(items);
    final p = _player;
    if (p != null) {
      await p.setAudioSources(
        tracks.map((t) => AudioSource.file(t.path)).toList(),
        initialIndex: index,
      );
    }
    mediaItem.add(items[index]);
    play();
  }

  MediaItem _toMediaItem(LocalTrack t, {String? albumName}) => MediaItem(
    id: t.id,
    title: t.title,
    artist: t.artist,
    album: albumName ?? 'TMusic MP3',
    artUri: t.artUrl != null ? Uri.tryParse(t.artUrl!) : null,
  );

  Future<void> playNext(LocalTrack track, {String? albumName}) async {
    if (!File(track.path).isAbsolute) {
      throw const FormatException('Đường dẫn không hợp lệ.');
    }
    if (_player != null && !await File(track.path).exists()) {
      throw const FormatException('File không còn tồn tại. Hãy thêm lại.');
    }
    final currentQueue = List<MediaItem>.from(queue.value);
    if (currentQueue.isEmpty || mediaItem.value == null) {
      await start([track], 0, albumName: albumName);
      return;
    }
    final currentIndex =
        _player?.currentIndex ?? playbackState.value.queueIndex ?? 0;
    final insertIndex = (currentIndex + 1).clamp(0, currentQueue.length);
    final item = _toMediaItem(track, albumName: albumName);
    final p = _player;
    if (p != null) {
      await p.insertAudioSource(insertIndex, AudioSource.file(track.path));
    }
    currentQueue.insert(insertIndex, item);
    queue.add(currentQueue);
  }

  Future<void> addToQueue(LocalTrack track, {String? albumName}) async {
    if (!File(track.path).isAbsolute) {
      throw const FormatException('Đường dẫn không hợp lệ.');
    }
    if (_player != null && !await File(track.path).exists()) {
      throw const FormatException('File không còn tồn tại. Hãy thêm lại.');
    }
    final currentQueue = List<MediaItem>.from(queue.value);
    if (currentQueue.isEmpty || mediaItem.value == null) {
      await start([track], 0, albumName: albumName);
      return;
    }
    final item = _toMediaItem(track, albumName: albumName);
    final p = _player;
    if (p != null) {
      await p.addAudioSource(AudioSource.file(track.path));
    }
    currentQueue.add(item);
    queue.add(currentQueue);
  }

  /// Safely removes a deleted song from active playback or queue without leaving broken audio state.
  Future<void> removeSongFromQueue(String songId) async {
    final targetId = 'song:$songId';
    final currentQueue = List<MediaItem>.from(queue.value);
    final matchingIndices = <int>[];
    for (var i = 0; i < currentQueue.length; i++) {
      if (currentQueue[i].id == targetId) matchingIndices.add(i);
    }
    if (matchingIndices.isEmpty) return;
    if (matchingIndices.length == currentQueue.length) {
      await stop();
      return;
    }
    final wasPlayingTarget = mediaItem.value?.id == targetId;
    final p = _player;
    for (final index in matchingIndices.reversed) {
      currentQueue.removeAt(index);
      if (p != null) {
        await p.removeAudioSourceAt(index);
      }
    }
    queue.add(currentQueue);
    if (wasPlayingTarget) {
      abLoop.value = null;
      final nextIndex = (p?.currentIndex ?? playbackState.value.queueIndex ?? 0)
          .clamp(0, currentQueue.length - 1);
      mediaItem.add(currentQueue[nextIndex]);
      if (p == null) {
        playbackState.add(
          playbackState.value.copyWith(
            queueIndex: nextIndex,
            updatePosition: Duration.zero,
          ),
        );
      }
    }
  }

  Future<void> seekRelative(Duration delta) async {
    final total = currentDuration;
    var target = currentPosition + delta;
    if (target < Duration.zero) target = Duration.zero;
    if (total != null && target > total) target = total;
    await seek(target);
  }

  Future<void> cycleRepeatMode() async {
    final next = switch (playbackState.value.repeatMode) {
      AudioServiceRepeatMode.none => AudioServiceRepeatMode.one,
      AudioServiceRepeatMode.one => AudioServiceRepeatMode.all,
      AudioServiceRepeatMode.all ||
      AudioServiceRepeatMode.group => AudioServiceRepeatMode.none,
    };
    await setRepeatMode(next);
  }

  void setAbLoop(Duration start, Duration end) {
    if (end <= start) return;
    abLoop.value = (start: start, end: end);
    if (currentPosition < start || currentPosition > end) {
      seek(start);
    }
  }

  void clearAbLoop() {
    abLoop.value = null;
  }

  Future<void> changeSpeed(double rate) async {
    final clamped = rate.clamp(0.5, 2.0);
    await _player?.setSpeed(clamped);
    speed.value = clamped;
    if (_player != null) _broadcast();
  }

  void _failed() => playbackState.add(
    playbackState.value.copyWith(
      playing: false,
      processingState: AudioProcessingState.error,
      errorMessage: 'Không đọc được file hoặc codec không được hỗ trợ.',
    ),
  );
  void _broadcast() {
    final p = _player;
    if (p == null) return;
    playbackState.add(
      PlaybackState(
        controls: [
          MediaControl.skipToPrevious,
          p.playing ? MediaControl.pause : MediaControl.play,
          MediaControl.skipToNext,
          MediaControl.stop,
        ],
        androidCompactActionIndices: const [0, 1, 2],
        systemActions: const {MediaAction.seek},
        playing: p.playing,
        processingState: switch (p.processingState) {
          ProcessingState.idle => AudioProcessingState.idle,
          ProcessingState.loading => AudioProcessingState.loading,
          ProcessingState.buffering => AudioProcessingState.buffering,
          ProcessingState.ready => AudioProcessingState.ready,
          ProcessingState.completed => AudioProcessingState.completed,
        },
        updatePosition: p.position,
        bufferedPosition: p.bufferedPosition,
        queueIndex: p.currentIndex,
        repeatMode: switch (p.loopMode) {
          LoopMode.off => AudioServiceRepeatMode.none,
          LoopMode.one => AudioServiceRepeatMode.one,
          LoopMode.all => AudioServiceRepeatMode.all,
        },
        shuffleMode: p.shuffleModeEnabled
            ? AudioServiceShuffleMode.all
            : AudioServiceShuffleMode.none,
      ),
    );
  }

  @override
  Future<void> play() async {
    final p = _player;
    if (p == null) {
      playbackState.add(
        playbackState.value.copyWith(
          playing: true,
          updatePosition: _testPosition,
        ),
      );
      return;
    }
    try {
      if (p.processingState == ProcessingState.completed) {
        await p.seek(Duration.zero);
      }
      await p.play();
    } catch (_) {
      _failed();
    }
  }

  @override
  Future<void> pause() async {
    final p = _player;
    if (p == null) {
      playbackState.add(
        playbackState.value.copyWith(
          playing: false,
          updatePosition: _testPosition,
        ),
      );
      return;
    }
    await p.pause();
  }

  @override
  Future<void> seek(Duration position) async {
    final p = _player;
    if (p == null) {
      _testPosition = position;
      _testPosCtrl.add(position);
      final range = abLoop.value;
      if (range != null && range.end > range.start && position >= range.end) {
        _testPosition = range.start;
        _testPosCtrl.add(range.start);
      }
      return;
    }
    await p.seek(position);
  }

  @override
  Future<void> skipToNext() async {
    final p = _player;
    if (p == null) {
      final items = queue.value;
      final next = (playbackState.value.queueIndex ?? 0) + 1;
      if (next < items.length) await skipToQueueItem(next);
      return;
    }
    await p.seekToNext();
  }

  @override
  Future<void> skipToPrevious() async {
    final p = _player;
    if (p == null) {
      final prev = (playbackState.value.queueIndex ?? 0) - 1;
      if (prev >= 0) await skipToQueueItem(prev);
      return;
    }
    await p.seekToPrevious();
  }

  @override
  Future<void> skipToQueueItem(int index) async {
    final p = _player;
    if (p == null) {
      final items = queue.value;
      if (index < 0 || index >= items.length) return;
      if (mediaItem.value?.id != items[index].id) abLoop.value = null;
      _testPosition = Duration.zero;
      _testPosCtrl.add(Duration.zero);
      mediaItem.add(items[index]);
      playbackState.add(
        playbackState.value.copyWith(
          queueIndex: index,
          updatePosition: Duration.zero,
        ),
      );
      return;
    }
    await p.seek(Duration.zero, index: index);
  }

  @override
  Future<void> stop() async {
    abLoop.value = null;
    await _player?.stop();
    queue.add([]);
    mediaItem.add(null);
    if (_player == null) {
      playbackState.add(
        playbackState.value.copyWith(
          playing: false,
          processingState: AudioProcessingState.idle,
        ),
      );
      return;
    }
    await super.stop();
  }

  @override
  Future<void> setRepeatMode(AudioServiceRepeatMode repeatMode) async {
    final p = _player;
    if (p == null) {
      playbackState.add(playbackState.value.copyWith(repeatMode: repeatMode));
      return;
    }
    await p.setLoopMode(switch (repeatMode) {
      AudioServiceRepeatMode.none => LoopMode.off,
      AudioServiceRepeatMode.one => LoopMode.one,
      _ => LoopMode.all,
    });
    _broadcast();
  }

  @override
  Future<void> setShuffleMode(AudioServiceShuffleMode shuffleMode) async {
    final p = _player;
    if (p == null) {
      playbackState.add(playbackState.value.copyWith(shuffleMode: shuffleMode));
      return;
    }
    if (shuffleMode != AudioServiceShuffleMode.none) await p.shuffle();
    await p.setShuffleModeEnabled(shuffleMode != AudioServiceShuffleMode.none);
    _broadcast();
  }
}
