import 'dart:async';
import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/features/playback/downloads_screen.dart';
import 'package:tmusic/features/playback/local_audio.dart';
import 'package:tmusic/shared/models/library_models.dart';

Map<String, dynamic> _session(String owner) => {
  'userId': owner,
  'accessToken': 'test-access',
  'refreshToken': 'test-refresh',
  'expiresAt': DateTime.now().add(const Duration(hours: 1)).toIso8601String(),
};

Map<String, dynamic> _job(String owner, {bool ready = false}) => {
  'id': 'job-$owner',
  'songId': 'song-$owner',
  'songTitle': 'Private $owner',
  'provider': 'soundcloud',
  'canonicalUrl': 'https://soundcloud.com/$owner/track',
  'status': ready ? 'COMPLETE' : 'QUEUED',
  'progressPercent': ready ? 100 : 0,
  'audioReady': ready,
};

class _DownloadApi extends ApiClient {
  _DownloadApi(this.owner);
  final String owner;
  final refreshStarted = Completer<void>();
  final bytesStarted = Completer<void>();
  Completer<Map<String, dynamic>>? pendingList;
  Completer<Uint8List>? pendingBytes;
  int listCalls = 0;
  bool ready = false;
  bool empty = false;

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (method == 'GET' && path == '/downloads') {
      listCalls++;
      if (pendingList != null) {
        if (!refreshStarted.isCompleted) refreshStarted.complete();
        return pendingList!.future;
      }
      return _page();
    }
    if (method == 'GET' && path == '/library/songs/song-$owner') {
      return {
        'id': 'song-$owner',
        'customTitle': 'Private $owner',
        'provider': 'soundcloud',
        'providerContentId': '$owner/track',
        'canonicalUrl': 'https://soundcloud.com/$owner/track',
        'audioReady': true,
      };
    }
    throw StateError('Unexpected $method $path for $owner');
  }

  Map<String, dynamic> _page() => {
    'items': empty ? [] : [_job(owner, ready: ready)],
    'total': empty ? 0 : 1,
    'page': 1,
    'pageSize': 30,
  };

  @override
  Future<Uint8List> fetchBytes(String path) async {
    if (!bytesStarted.isCompleted) bytesStarted.complete();
    return pendingBytes!.future;
  }
}

class _DeviceLibrary extends LocalLibrary {
  _DeviceLibrary() : super(null, null);
  final savedSongIds = <String>[];

  @override
  Future<bool> hasDownloadedSong(String songId) async =>
      savedSongIds.contains(songId);

  @override
  Future<LocalTrack> saveDownloadedSongMp3(Song song, Uint8List bytes) async {
    savedSongIds.add(song.id);
    return LocalTrack('song:${song.id}', song.title, '/device/${song.id}.mp3');
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  test(
    'same-account polling does not discard an MP3 transfer already in flight',
    () async {
      final api = _DownloadApi('same')
        ..ready = true
        ..pendingBytes = Completer<Uint8List>();
      final local = _DeviceLibrary();
      final c = ProviderContainer(
        overrides: [
          authorizedApiProvider.overrideWithValue(api),
          localLibraryProvider.overrideWith((ref) async => local),
        ],
      );
      addTearDown(c.dispose);
      final subscription = c.listen(downloadJobsProvider, (_, _) {});
      addTearDown(subscription.close);
      await c.read(sessionProvider.future);
      await c.read(sessionProvider.notifier).accept(_session('same'));
      await c.read(downloadJobsProvider.future);
      await api.bytesStarted.future;
      api.pendingList = Completer<Map<String, dynamic>>();
      final refresh = c
          .read(downloadJobsProvider.notifier)
          .refresh(silent: true);
      await api.refreshStarted.future;

      api.pendingBytes!.complete(Uint8List(64));
      await Future<void>.delayed(Duration.zero);

      expect(local.savedSongIds, ['song-same']);
      api.pendingList!.complete(api._page());
      await refresh;
      expect(local.savedSongIds, ['song-same']);
    },
  );

  test('late download refresh cannot expose previous account jobs or restart its timer', () async {
    final oldApi = _DownloadApi('old');
    final newApi = _DownloadApi('new')
      ..empty = true
      ..pendingList = Completer<Map<String, dynamic>>();
    final c = ProviderContainer(
      overrides: [
        authorizedApiProvider.overrideWith(
          (ref) => ref.watch(sessionProvider).value?.userId == 'old'
              ? oldApi
              : newApi,
        ),
      ],
    );
    addTearDown(c.dispose);
    final subscription = c.listen(downloadJobsProvider, (_, _) {});
    addTearDown(subscription.close);
    await c.read(sessionProvider.future);
    await c.read(sessionProvider.notifier).accept(_session('old'));
    await c.read(downloadJobsProvider.future);
    oldApi.pendingList = Completer<Map<String, dynamic>>();
    final pending = c.read(downloadJobsProvider.notifier).refresh(silent: true);
    await oldApi.refreshStarted.future;

    await c.read(sessionProvider.notifier).accept(_session('new'));
    final next = c.read(downloadJobsProvider.future);
    await newApi.refreshStarted.future;
    expect(c.read(downloadJobsProvider).value, isEmpty);
    newApi.pendingList!.complete(newApi._page());
    await next;
    oldApi.pendingList!.complete(oldApi._page());
    await pending;

    expect(c.read(downloadJobsProvider).value, isEmpty);
    await Future<void>.delayed(const Duration(milliseconds: 1500));
    expect(oldApi.listCalls, 2);
    expect(newApi.listCalls, 1);
  });

  test(
    'MP3 bytes completing after logout are discarded before a device write',
    () async {
      final api = _DownloadApi('old')
        ..ready = true
        ..pendingBytes = Completer<Uint8List>();
      final local = _DeviceLibrary();
      final c = ProviderContainer(
        overrides: [
          authorizedApiProvider.overrideWithValue(api),
          localLibraryProvider.overrideWith((ref) async => local),
        ],
      );
      addTearDown(c.dispose);
      final subscription = c.listen(downloadJobsProvider, (_, _) {});
      addTearDown(subscription.close);
      await c.read(sessionProvider.future);
      await c.read(sessionProvider.notifier).accept(_session('old'));
      await c.read(downloadJobsProvider.future);
      await api.bytesStarted.future;

      await c.read(sessionProvider.notifier).clear();
      await c.read(downloadJobsProvider.future);
      api.pendingBytes!.complete(Uint8List(64));
      await Future<void>.delayed(Duration.zero);

      expect(local.savedSongIds, isEmpty);
      expect(c.read(downloadJobsProvider).value, isEmpty);
    },
  );
}
