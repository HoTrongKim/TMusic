import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';

import '../../shared/models/library_models.dart';
import '../api/api_client.dart';

final guestLibraryStoreProvider = Provider<GuestLibraryStore>(
  (_) => GuestLibraryStore(),
);

/// Local-only Guest Library persisted inside the app's private sandbox (`allowBackup=false`).
/// If the user uninstalls the app without logging in, all guest albums & songs are wiped.
/// When the user signs in or registers, GuestLibraryStore is automatically synced to their account.
class GuestLibraryStore {
  Map<String, dynamic>? _memoryCache;
  int _revision = 0;
  Future<void> _pendingWrite = Future.value();

  int get revision => _revision;

  static String _newGuestId(String prefix) =>
      '$prefix-${DateTime.now().microsecondsSinceEpoch}-${Random.secure().nextInt(999999)}';

  Future<File?> _file() async {
    if (Platform.environment.containsKey('FLUTTER_TEST')) return null;
    try {
      final dir = await getApplicationSupportDirectory();
      return File('${dir.path}/guest_library_v1.json');
    } catch (_) {
      return null;
    }
  }

  Future<Map<String, dynamic>> _readState() async {
    if (_memoryCache != null) return _memoryCache!;
    final f = await _file();
    if (f != null && await f.exists()) {
      try {
        final decoded = jsonDecode(await f.readAsString());
        if (decoded is Map) {
          _memoryCache ??= Map<String, dynamic>.from(decoded);
          return _memoryCache!;
        }
      } catch (_) {}
    }
    _memoryCache ??= {
      'albums': <Map<String, dynamic>>[],
      'songs': <Map<String, dynamic>>[],
      'albumOrders': <String, List<String>>{},
    };
    return _memoryCache!;
  }

  Future<void> _writeState(Map<String, dynamic> next) {
    _memoryCache = next;
    _revision++;
    final document = jsonEncode(next);
    final write = _pendingWrite.then((_) async {
      final file = await _file();
      if (file != null) {
        final temp = await File('${file.path}.tmp')
            .writeAsString(document, flush: true);
        await temp.rename(file.path);
      }
    });
    _pendingWrite = write.then((_) {}, onError: (Object _, StackTrace _) {});
    return write;
  }

  Future<bool> hasAnyData() async {
    final s = await _readState();
    final albums = s['albums'] as List? ?? const [];
    final songs = s['songs'] as List? ?? const [];
    return albums.isNotEmpty || songs.isNotEmpty;
  }

  Future<(List<Album>, List<Song>)> exportAll() async {
    final s = await _readState();
    final rawSongs = (s['songs'] as List? ?? const [])
        .map((e) => Song.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
    final rawAlbums = (s['albums'] as List? ?? const []).map((e) {
      final m = Map<String, dynamic>.from(e as Map);
      final id = m['id'] as String;
      m['songCount'] = rawSongs.where((x) => x.albumIds.contains(id)).length;
      return Album.fromJson(m);
    }).toList();
    return (rawAlbums, rawSongs);
  }

  Future<bool> clearAfterSync(int expectedRevision) async {
    await _pendingWrite;
    final file = await _file();
    if (_revision != expectedRevision) return false;
    final empty = {
      'albums': <Map<String, dynamic>>[],
      'songs': <Map<String, dynamic>>[],
      'albumOrders': <String, List<String>>{},
    };
    if (file != null && file.existsSync()) file.deleteSync();
    _memoryCache = empty;
    _revision++;
    return true;
  }

  Future<PageResult<Album>> listAlbums({
    String query = '',
    String sort = 'recent',
    bool? favorite,
  }) async {
    final (allAlbums, _) = await exportAll();
    final q = query.trim().toLowerCase();
    var filtered = allAlbums.where((a) {
      if (favorite != null && a.isFavorite != favorite) return false;
      if (q.isNotEmpty &&
          !a.name.toLowerCase().contains(q) &&
          !(a.description ?? '').toLowerCase().contains(q)) {
        return false;
      }
      return true;
    }).toList();

    if (sort == 'name') {
      filtered.sort((a, b) => a.name.compareTo(b.name));
    } else if (sort == 'order') {
      filtered.sort((a, b) => a.sortOrder.compareTo(b.sortOrder));
    }
    return PageResult(filtered, filtered.length, 1, max(50, filtered.length));
  }

  Future<Album> getAlbum(String id) async {
    final (allAlbums, _) = await exportAll();
    final match = allAlbums.where((a) => a.id == id).firstOrNull;
    if (match == null) throw const ApiFailure('not_found', status: 404);
    return match;
  }

  Future<Album> saveAlbum({
    Album? existing,
    required String name,
    required String description,
    String? cover,
    bool? favorite,
    int? sortOrder,
  }) async {
    final s = await _readState();
    final list = List<Map<String, dynamic>>.from(
      (s['albums'] as List? ?? const []).map(
        (e) => Map<String, dynamic>.from(e as Map),
      ),
    );
    if (existing == null) {
      final created = Album(
        id: _newGuestId('guest-alb'),
        name: name,
        version: 'local',
        description: description.isEmpty ? null : description,
        coverImageUrl: cover ?? 'cover:green',
        isFavorite: favorite ?? false,
        sortOrder: sortOrder ?? list.length,
        songCount: 0,
      );
      list.insert(0, created.toJson());
      s['albums'] = list;
      await _writeState(s);
      return created;
    }

    final idx = list.indexWhere((x) => x['id'] == existing.id);
    if (idx < 0) throw const ApiFailure('not_found', status: 404);
    final updated = Album(
      id: existing.id,
      name: name,
      version: 'local',
      description: description.isEmpty ? null : description,
      coverImageUrl: cover,
      isFavorite: favorite ?? existing.isFavorite,
      sortOrder: sortOrder ?? existing.sortOrder,
      songCount: existing.songCount,
    );
    list[idx] = updated.toJson();
    s['albums'] = list;
    await _writeState(s);
    return getAlbum(existing.id);
  }

  Future<void> reorderAlbums(List<String> ids) async {
    final s = await _readState();
    final list = List<Map<String, dynamic>>.from(
      (s['albums'] as List? ?? const []).map(
        (e) => Map<String, dynamic>.from(e as Map),
      ),
    );
    for (var i = 0; i < ids.length; i++) {
      final idx = list.indexWhere((a) => a['id'] == ids[i]);
      if (idx >= 0) list[idx]['sortOrder'] = i;
    }
    s['albums'] = list;
    await _writeState(s);
  }

  Future<void> deleteAlbum(String albumId) async {
    final s = await _readState();
    final albums = List<Map<String, dynamic>>.from(
      (s['albums'] as List? ?? const []).map(
        (e) => Map<String, dynamic>.from(e as Map),
      ),
    )..removeWhere((a) => a['id'] == albumId);
    final songs = List<Map<String, dynamic>>.from(
      (s['songs'] as List? ?? const []).map(
        (e) => Map<String, dynamic>.from(e as Map),
      ),
    );
    for (final song in songs) {
      final ids = (song['albumIds'] as List? ?? const [])
          .map((e) => '$e')
          .where((id) => id != albumId)
          .toList();
      song['albumIds'] = ids;
    }
    s['albums'] = albums;
    s['songs'] = songs;
    await _writeState(s);
  }

  Future<PageResult<Song>> listSongs({
    String query = '',
    String? provider,
    bool? favorite,
    bool? unorganized,
    String? albumId,
    String sort = 'recent',
  }) async {
    final s = await _readState();
    final (_, allSongs) = await exportAll();
    final q = query.trim().toLowerCase();
    var filtered = allSongs.where((song) {
      if (albumId != null && !song.albumIds.contains(albumId)) return false;
      if (provider != null &&
          provider.isNotEmpty &&
          song.provider != provider) {
        return false;
      }
      if (favorite != null && song.favorite != favorite) return false;
      if (unorganized == true && song.albumIds.isNotEmpty) return false;
      if (unorganized == false && song.albumIds.isEmpty) return false;
      if (q.isNotEmpty &&
          !song.title.toLowerCase().contains(q) &&
          !(song.artist ?? '').toLowerCase().contains(q) &&
          !(song.originalCreator ?? '').toLowerCase().contains(q)) {
        return false;
      }
      return true;
    }).toList();

    if (albumId != null) {
      final orders = s['albumOrders'] as Map? ?? const {};
      final orderList = (orders[albumId] as List? ?? const [])
          .map((e) => '$e')
          .toList();
      if (orderList.isNotEmpty) {
        filtered.sort((a, b) {
          final ia = orderList.indexOf(a.id);
          final ib = orderList.indexOf(b.id);
          return (ia < 0 ? 9999 : ia).compareTo(ib < 0 ? 9999 : ib);
        });
      }
    } else if (sort == 'name') {
      filtered.sort((a, b) => a.title.compareTo(b.title));
    }
    return PageResult(filtered, filtered.length, 1, max(50, filtered.length));
  }

  Future<Song> getSong(String id) async {
    final (_, allSongs) = await exportAll();
    final match = allSongs.where((s) => s.id == id).firstOrNull;
    if (match == null) throw const ApiFailure('not_found', status: 404);
    return match;
  }

  Future<Song?> findByCanonicalUrl(String canonicalUrl) async {
    final (_, allSongs) = await exportAll();
    return allSongs.where((s) => s.url == canonicalUrl).firstOrNull;
  }

  Future<Song> saveSong({
    required String url,
    required String provider,
    required String providerContentId,
    required String title,
    String? artist,
    String? originalTitle,
    String? originalCreator,
    String? thumbnail,
    int? durationSeconds,
    required List<String> albumIds,
    bool audioReady = false,
  }) async {
    final s = await _readState();
    final list = List<Map<String, dynamic>>.from(
      (s['songs'] as List? ?? const []).map(
        (e) => Map<String, dynamic>.from(e as Map),
      ),
    );
    final existingIdx = list.indexWhere(
      (x) =>
          x['provider'] == provider &&
          ((providerContentId.isNotEmpty &&
                  x['providerContentId'] == providerContentId) ||
              // Legacy guest shares used the canonical URL as their content ID.
              // Keep the exact URL case: YouTube IDs are case-sensitive.
              x['canonicalUrl'] == url),
    );
    if (existingIdx >= 0) {
      final cur = Song.fromJson(list[existingIdx]);
      final mergedAlbums = {...cur.albumIds, ...albumIds}.toList();
      final updated = cur.copyWith(
        albumIds: mergedAlbums,
        audioReady: cur.audioReady || audioReady,
        audioStatus: (cur.audioReady || audioReady) ? 'READY' : cur.audioStatus,
      );
      list[existingIdx] = updated.toJson();
      s['songs'] = list;
      await _writeState(s);
      return updated;
    }

    final created = Song(
      id: _newGuestId('guest-sng'),
      version: 'local',
      title: title,
      customTitle: title,
      artist: artist?.trim().isEmpty == true ? null : artist?.trim(),
      provider: provider,
      providerContentId: providerContentId,
      url: url,
      originalUrl: url,
      originalTitle: originalTitle,
      originalCreator: originalCreator,
      thumbnail: thumbnail,
      durationSeconds: durationSeconds,
      capability: 'EXTERNAL_OPEN_ONLY',
      audioReady: audioReady,
      audioStatus: audioReady ? 'READY' : 'NONE',
      albumIds: albumIds.toSet().toList(),
    );
    list.insert(0, created.toJson());
    s['songs'] = list;
    await _writeState(s);
    return created;
  }

  Future<Song> updateSong(
    String id, {
    String? title,
    String? artist,
    bool? favorite,
    bool? audioReady,
    String? audioStatus,
    List<String>? albumIds,
  }) async {
    final s = await _readState();
    final list = List<Map<String, dynamic>>.from(
      (s['songs'] as List? ?? const []).map(
        (e) => Map<String, dynamic>.from(e as Map),
      ),
    );
    final idx = list.indexWhere((x) => x['id'] == id);
    if (idx < 0) throw const ApiFailure('not_found', status: 404);
    final cur = Song.fromJson(list[idx]);
    final next = cur.copyWith(
      title: title,
      customTitle: title,
      artist: artist,
      favorite: favorite,
      audioReady: audioReady,
      audioStatus:
          audioStatus ??
          (audioReady == null
              ? cur.audioStatus
              : (audioReady ? 'READY' : 'NONE')),
      albumIds: albumIds,
    );
    list[idx] = next.toJson();
    s['songs'] = list;
    await _writeState(s);
    return next;
  }

  Future<void> deleteSong(String id) async {
    final s = await _readState();
    final list = List<Map<String, dynamic>>.from(
      (s['songs'] as List? ?? const []).map(
        (e) => Map<String, dynamic>.from(e as Map),
      ),
    )..removeWhere((x) => x['id'] == id);
    s['songs'] = list;
    await _writeState(s);
  }

  Future<void> orderAlbumSongs(String albumId, List<String> songIds) async {
    final s = await _readState();
    final orders = Map<String, dynamic>.from(
      s['albumOrders'] as Map? ?? const {},
    );
    orders[albumId] = songIds;
    s['albumOrders'] = orders;
    await _writeState(s);
  }
}
