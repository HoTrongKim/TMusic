import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sqflite/sqflite.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/core/storage/guest_library_store.dart';
import 'package:tmusic/core/storage/library_cache.dart';
import 'package:tmusic/features/library/library_repository.dart';
import 'package:tmusic/features/playback/downloads_screen.dart';
import 'package:tmusic/features/playback/local_audio.dart';
import 'package:tmusic/shared/models/library_models.dart';

class _GuestSession extends SessionController {
  @override
  Future<Session?> build() async => null;
}

class _UnavailableExtractor extends ApiClient {
  bool failExtraction = true;

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (method == 'POST' && path == '/guest/songs/resolve') {
      return {
        'provider': 'soundcloud',
        'providerContentId': 'artist/track',
        'canonicalUrl': 'https://soundcloud.com/artist/track',
        'originalTitle': 'Source track',
        'originalCreator': 'Source artist',
        'playbackCapability': 'EXTERNAL_OPEN_ONLY',
      };
    }
    throw StateError('Unexpected $method $path');
  }

  @override
  Future<Uint8List> postBytes(String path, {Map<String, dynamic>? data}) async {
    if (path != '/guest/songs/extract-mp3' ||
        data?['rightsConfirmed'] != true) {
      throw StateError('Unexpected extraction request');
    }
    if (failExtraction) throw const ApiFailure('network_error');
    return Uint8List.fromList([0x49, 0x44, 0x33, ...List.filled(61, 0)]);
  }
}

class _ClaimApi extends ApiClient {
  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (method == 'POST' && path == '/library/songs') {
      return {
        'id': 'cloud-song',
        'customTitle': data?['customTitle'],
        'provider': 'soundcloud',
        'providerContentId': 'artist/track',
        'canonicalUrl': data?['url'],
        'originalCreator': 'Source artist',
        'audioReady': false,
      };
    }
    throw StateError('Unexpected $method $path');
  }
}

Future<Song> _guestSong(GuestLibraryStore store, {bool ready = false}) =>
    store.saveSong(
      url: 'https://soundcloud.com/artist/track',
      provider: 'soundcloud',
      providerContentId: 'artist/track',
      title: 'Nhạc được phép tải',
      originalCreator: 'Source artist',
      albumIds: [],
      audioReady: ready,
    );

ProviderContainer _container(GuestLibraryStore store) => ProviderContainer(
  overrides: [
    sessionProvider.overrideWith(_GuestSession.new),
    guestLibraryStoreProvider.overrideWithValue(store),
    publicApiProvider.overrideWithValue(_UnavailableExtractor()),
  ],
);

// SQLite's platform boundary is replaced; production file copies/deletions stay real.
class _TrackDatabase implements Database {
  final rows = <String, Map<String, Object?>>{};
  bool failInsert = false;

  @override
  dynamic noSuchMethod(Invocation invocation) {
    if (invocation.memberName == #query) {
      return Future.value(rows.values.map(Map<String, Object?>.from).toList());
    }
    if (invocation.memberName == #insert) {
      if (failInsert) throw const FileSystemException('Storage full');
      final row = Map<String, Object?>.from(invocation.positionalArguments[1]);
      rows[row['id']! as String] = row;
      return Future.value(1);
    }
    if (invocation.memberName == #delete) {
      final args = invocation.namedArguments[#whereArgs] as List?;
      return Future.value(rows.remove(args?.first) == null ? 0 : 1);
    }
    return super.noSuchMethod(invocation);
  }
}

Future<LocalTrack> _importedTrack(LocalLibrary library, String id) async {
  final file = await File('${library.directory!.path}/$id.mp3')
      .writeAsBytes(List.generate(64, (i) => i));
  await library.db!.insert('tracks', {
    'id': id,
    'title': 'Bản ghi cá nhân',
    'path': file.path,
    'created': 1,
  });
  return LocalTrack(id, 'Bản ghi cá nhân', file.path);
}

LibraryRepository _repoWithAudio(
  ProviderContainer container,
  GuestLibraryStore store,
  LocalLibrary guest,
  LocalLibrary account, {
  bool isGuest = false,
  ApiClient? publicApi,
  ApiClient? api,
}) => LibraryRepository(
  api: api ?? _UnavailableExtractor(),
  publicApi: publicApi ?? _UnavailableExtractor(),
  cache: LibraryCache(isGuest ? null : 'account-1'),
  guestStore: store,
  offline: container.read(offlineModeProvider.notifier),
  isGuest: isGuest,
  currentUserId: () => isGuest ? null : 'account-1',
  openLocalLibrary: (owner) async {
    if (owner == 'guest') return guest;
    if (owner == 'account-1') return account;
    throw StateError('Unexpected local audio owner');
  },
);

void main() {
  test(
    'claim copies guest imported files to account only on this device',
    () async {
      final temp = await Directory.systemTemp.createTemp('tmusic-claim-');
      addTearDown(() => temp.delete(recursive: true));
      final guest = LocalLibrary(
        await Directory('${temp.path}/guest').create(),
        _TrackDatabase(),
      );
      final account = LocalLibrary(
        await Directory('${temp.path}/account').create(),
        _TrackDatabase(),
      );
      await _importedTrack(guest, 'imported-recording');
      final store = GuestLibraryStore();
      final container = _container(store);
      addTearDown(container.dispose);
      final repo = _repoWithAudio(container, store, guest, account);

      expect(await repo.syncGuestDataToCloud(userId: 'account-1'), isTrue);

      final copied = (await account.list()).single;
      expect(copied.title, 'Bản ghi cá nhân');
      expect(
        await File(copied.path).readAsBytes(),
        List.generate(64, (i) => i),
      );
      expect(copied.path.startsWith(account.directory!.path), isTrue);
      expect(await guest.list(), isEmpty);
      expect(await repo.syncGuestDataToCloud(userId: 'account-1'), isFalse);
      expect(await account.list(), hasLength(1));
    },
  );

  test('failed device claim retains imported guest files for retry', () async {
    final temp = await Directory.systemTemp.createTemp('tmusic-claim-failure-');
    addTearDown(() => temp.delete(recursive: true));
    final guest = LocalLibrary(
      await Directory('${temp.path}/guest').create(),
      _TrackDatabase(),
    );
    final targetDb = _TrackDatabase()..failInsert = true;
    final account = LocalLibrary(
      await Directory('${temp.path}/account').create(),
      targetDb,
    );
    final source = await _importedTrack(guest, 'imported-recording');
    final store = GuestLibraryStore();
    final container = _container(store);
    addTearDown(container.dispose);
    final repo = _repoWithAudio(container, store, guest, account);

    expect(await repo.syncGuestDataToCloud(userId: 'account-1'), isFalse);
    expect(await File(source.path).exists(), isTrue);
    expect(await guest.list(), hasLength(1));
    targetDb.failInsert = false;

    expect(await repo.syncGuestDataToCloud(userId: 'account-1'), isTrue);
    expect(await account.list(), hasLength(1));
  });

  test('claim removes guest MP3 only after durable account copy and keeps failures retryable', () async {
    final temp = await Directory.systemTemp.createTemp('tmusic-claim-mp3-');
    addTearDown(() => temp.delete(recursive: true));
    final guest = LocalLibrary(
      await Directory('${temp.path}/guest').create(),
      _TrackDatabase(),
    );
    final targetDb = _TrackDatabase()..failInsert = true;
    final account = LocalLibrary(
      await Directory('${temp.path}/account').create(),
      targetDb,
    );
    final store = GuestLibraryStore();
    final saved = await _guestSong(store, ready: true);
    final source = await guest.saveDownloadedSongMp3(
      saved,
      Uint8List.fromList(List.filled(64, 1)),
    );
    final container = _container(store);
    addTearDown(container.dispose);
    final repo = _repoWithAudio(
      container,
      store,
      guest,
      account,
      api: _ClaimApi(),
    );

    expect(await repo.syncGuestDataToCloud(userId: 'account-1'), isFalse);
    expect(await File(source.path).exists(), isTrue);
    expect((await store.listSongs()).items, hasLength(1));
    targetDb.failInsert = false;

    expect(await repo.syncGuestDataToCloud(userId: 'account-1'), isTrue);
    expect(await account.hasDownloadedSong('cloud-song'), isTrue);
    expect(await guest.list(), isEmpty);
    expect(await File(source.path).exists(), isFalse);
    expect(await store.hasAnyData(), isFalse);
  });

  test('guest completed downloads are available on initial load', () async {
    final store = GuestLibraryStore();
    final saved = await _guestSong(store, ready: true);
    final container = _container(store);
    addTearDown(container.dispose);
    await container.read(sessionProvider.future);

    final jobs = await container.read(downloadJobsProvider.future);

    expect(jobs.single.songId, saved.id);
    expect(jobs.single.status, 'COMPLETE');
  });

  test(
    'deleting a guest download retains source and clears audio state',
    () async {
      final store = GuestLibraryStore();
      final saved = await _guestSong(store, ready: true);
      final container = _container(store);
      addTearDown(container.dispose);
      await container.read(sessionProvider.future);
      final repo = container.read(libraryRepositoryProvider);

      await repo.cancelOrDeleteDownloadJob('guest-job-${saved.id}');

      final song = await store.getSong(saved.id);
      expect(song.url, 'https://soundcloud.com/artist/track');
      expect(song.audioReady, isFalse);
      expect(song.audioStatus, 'NONE');
      expect((await repo.downloadJobs()).items, isEmpty);
    },
  );

  test(
    'guest retry saves a device file and delete removes its bytes',
    () async {
      final temp = await Directory.systemTemp.createTemp('tmusic-guest-retry-');
      addTearDown(() => temp.delete(recursive: true));
      final local = LocalLibrary(temp, _TrackDatabase());
      final store = GuestLibraryStore();
      final container = _container(store);
      addTearDown(container.dispose);
      final api = _UnavailableExtractor();
      final repo = _repoWithAudio(
        container,
        store,
        local,
        local,
        isGuest: true,
        publicApi: api,
      );
      final saved = await repo.saveLink(
        'https://soundcloud.com/artist/track',
        'Nhạc được phép tải',
        '',
        [],
        downloadMp3: true,
        rightsConfirmed: true,
      );
      api.failExtraction = false;

      final completed = await repo.retryDownloadJob('guest-job-${saved.id}');
      final file = File((await local.getDownloadedSongTrack(saved))!.path);
      expect(completed.status, 'COMPLETE');
      expect((await store.getSong(saved.id)).audioStatus, 'READY');
      expect(await file.exists(), isTrue);

      await repo.cancelOrDeleteDownloadJob(completed.id);

      expect(await file.exists(), isFalse);
      expect(await local.list(), isEmpty);
      expect((await store.getSong(saved.id)).audioReady, isFalse);
      expect((await repo.downloadJobs()).items, isEmpty);
    },
  );

  test(
    'failed guest extraction retains an actionable failed download',
    () async {
      final store = GuestLibraryStore();
      final container = _container(store);
      addTearDown(container.dispose);
      await container.read(sessionProvider.future);
      final repo = container.read(libraryRepositoryProvider);

      final saved = await repo.saveLink(
        'https://soundcloud.com/artist/track',
        'Nhạc được phép tải',
        '',
        [],
        downloadMp3: true,
        rightsConfirmed: true,
      );

      expect(saved.audioReady, isFalse);
      expect(saved.audioStatus, 'FAILED');
      expect((await store.exportAll()).$2.single.id, saved.id);
      final job = (await repo.downloadJobs()).items.single;
      expect(job.status, 'FAILED');
      expect(job.songId, saved.id);

      await expectLater(
        repo.retryDownloadJob(job.id),
        throwsA(
          isA<ApiFailure>().having((e) => e.code, 'code', 'network_error'),
        ),
      );
      expect((await repo.downloadJobs()).items.single.status, 'FAILED');
    },
  );

  testWidgets('guest downloads show failed extraction and retry control', (
    tester,
  ) async {
    final store = GuestLibraryStore();
    final container = _container(store);
    addTearDown(container.dispose);
    await container.read(sessionProvider.future);
    await container
        .read(libraryRepositoryProvider)
        .saveLink(
          'https://soundcloud.com/artist/track',
          'Nhạc được phép tải',
          '',
          [],
          downloadMp3: true,
          rightsConfirmed: true,
        );

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: const MaterialApp(
          locale: Locale('vi'),
          supportedLocales: [Locale('vi')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: DownloadsScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Nhạc được phép tải'), findsOneWidget);
    expect(find.textContaining('Chưa trích xuất được MP3'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Thử lại'), findsOneWidget);
  });
}
