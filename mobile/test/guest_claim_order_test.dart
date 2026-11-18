import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/storage/guest_library_store.dart';
import 'package:tmusic/core/storage/library_cache.dart';
import 'package:tmusic/features/library/library_repository.dart';
import 'package:tmusic/features/playback/local_audio.dart';
import 'package:tmusic/shared/models/library_models.dart';

class _ClaimApi extends ApiClient {
  final albumsByKey = <String, String>{};
  final memberships = <String, List<String>>{};
  final songByUrl = <String, String>{};
  final songUrls = <String, String>{};
  final versions = <String, int>{};
  bool failOrder = false;
  int orderWrites = 0;
  int membershipPages = 0;
  void Function()? onMembershipRead;
  Future<void> Function()? onOrder;

  void seedSong(String url, String id) {
    songByUrl[url] = id;
    songUrls[id] = url;
  }

  void seedAlbum(String guestId, List<String> songIds) {
    final id = 'cloud-album-${albumsByKey.length}';
    albumsByKey[guestId] = id;
    memberships[id] = List.of(songIds);
    versions[id] = 1;
  }

  Map<String, dynamic> albumJson(String id) => {
    'id': id,
    'name': 'Album claimed',
    'version': 'v${versions[id]}',
    'songCount': memberships[id]!.length,
  };

  Map<String, dynamic> songJson(String id) => {
    'id': id,
    'version': 'song-version',
    'customTitle': id,
    'provider': 'youtube',
    'canonicalUrl': songUrls[id] ?? 'https://youtu.be/ExtraSong01',
    'albumIds': [
      for (final entry in memberships.entries)
        if (entry.value.contains(id)) entry.key,
    ],
  };

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (method == 'POST' && path == '/albums') {
      final key = headers!['Idempotency-Key'] as String;
      if (!albumsByKey.containsKey(key)) seedAlbum(key, []);
      return albumJson(albumsByKey[key]!);
    }
    if (method == 'POST' && path == '/library/songs') {
      final url = data!['url'] as String;
      final id = songByUrl[url] ?? 'cloud-song-${songByUrl.length}';
      seedSong(url, id);
      for (final album in data['albumIds'] as List) {
        final members = memberships[album]!;
        if (!members.contains(id)) {
          members.add(id);
          versions[album] = versions[album]! + 1;
        }
      }
      return songJson(id);
    }
    final parts = path.split('/');
    if (parts.length >= 3 && parts[1] == 'albums') {
      final id = parts[2];
      if (method == 'GET' && parts.length == 3) {
        return albumJson(id);
      }
      if (method == 'GET' && parts.length == 4 && parts[3] == 'songs') {
        membershipPages++;
        onMembershipRead?.call();
        final page = query?['page'] as int? ?? 1;
        final pageSize = query?['pageSize'] as int? ?? 30;
        final members = memberships[id]!;
        return {
          'items': members
              .skip((page - 1) * pageSize)
              .take(pageSize)
              .map(songJson)
              .toList(),
          'total': members.length,
          'page': page,
          'pageSize': pageSize,
        };
      }
      if (method == 'PATCH' && parts.length == 5 && parts[4] == 'order') {
        if (failOrder) {
          throw const ApiFailure('CONCURRENCY_CONFLICT', status: 412);
        }
        if (version != 'v${versions[id]}') {
          throw const ApiFailure('CONCURRENCY_CONFLICT', status: 412);
        }
        final ordered = List<String>.from(data!['songIds'] as List);
        expect(ordered.toSet(), memberships[id]!.toSet());
        expect(ordered.length, memberships[id]!.length);
        orderWrites++;
        memberships[id] = ordered;
        versions[id] = versions[id]! + 1;
        await onOrder?.call();
        return {};
      }
    }
    throw StateError('Unexpected $method $path');
  }
}

Future<(Album, Song, Song)> _guestAlbum(GuestLibraryStore store) async {
  final album = await store.saveAlbum(name: 'Guest album', description: '');
  final first = await store.saveSong(
    url: 'https://www.youtube.com/watch?v=GuestSong01',
    provider: 'youtube',
    providerContentId: 'GuestSong01',
    title: 'A',
    albumIds: [album.id],
  );
  final second = await store.saveSong(
    url: 'https://www.youtube.com/watch?v=GuestSong02',
    provider: 'youtube',
    providerContentId: 'GuestSong02',
    title: 'B',
    albumIds: [album.id],
  );
  await store.orderAlbumSongs(album.id, [second.id, first.id]);
  return (album, first, second);
}

LibraryRepository _repository(
  GuestLibraryStore store,
  _ClaimApi api, {
  String? Function()? owner,
}) {
  final container = ProviderContainer();
  addTearDown(container.dispose);
  return LibraryRepository(
    api: api,
    publicApi: api,
    cache: LibraryCache('account'),
    guestStore: store,
    offline: container.read(offlineModeProvider.notifier),
    isGuest: false,
    currentUserId: owner ?? () => 'account',
    openLocalLibrary: (_) async => LocalLibrary(null, null),
  );
}

void main() {
  test(
    'claim preserves the guest song order independently for each album',
    () async {
      final store = GuestLibraryStore();
      final (album, first, second) = await _guestAlbum(store);
      final other = await store.saveAlbum(
        name: 'Second album',
        description: '',
      );
      await store.updateSong(first.id, albumIds: [album.id, other.id]);
      await store.updateSong(second.id, albumIds: [album.id, other.id]);
      await store.orderAlbumSongs(other.id, [first.id, second.id]);
      final api = _ClaimApi();

      expect(
        await _repository(store, api).syncGuestDataToCloud(userId: 'account'),
        isTrue,
      );

      expect(api.memberships[api.albumsByKey[album.id]], [
        api.songByUrl[second.url],
        api.songByUrl[first.url],
      ]);
      expect(api.memberships[api.albumsByKey[other.id]], [
        api.songByUrl[first.url],
        api.songByUrl[second.url],
      ]);
      expect(await store.hasAnyData(), isFalse);
    },
  );

  test('claim retains duplicate account songs and every existing paginated membership', () async {
    final store = GuestLibraryStore();
    final (album, first, second) = await _guestAlbum(store);
    final api = _ClaimApi()..seedSong(first.url, 'existing-song');
    final extras = List.generate(35, (i) => 'extra-$i');
    api.seedAlbum(album.id, ['existing-song', ...extras]);

    expect(
      await _repository(store, api).syncGuestDataToCloud(userId: 'account'),
      isTrue,
    );

    expect(api.memberships[api.albumsByKey[album.id]], [
      api.songByUrl[second.url],
      'existing-song',
      ...extras,
    ]);
    expect(api.songByUrl[first.url], 'existing-song');
    expect(api.membershipPages, greaterThan(1));
  });

  test('failed order claim retains guest data and can retry without duplicate albums', () async {
    final store = GuestLibraryStore();
    final (album, first, second) = await _guestAlbum(store);
    final api = _ClaimApi()..failOrder = true;
    final repo = _repository(store, api);

    expect(await repo.syncGuestDataToCloud(userId: 'account'), isFalse);
    expect(await store.hasAnyData(), isTrue);
    api.failOrder = false;
    expect(await repo.syncGuestDataToCloud(userId: 'account'), isTrue);

    expect(api.albumsByKey, hasLength(1));
    expect(api.memberships[api.albumsByKey[album.id]], [
      api.songByUrl[second.url],
      api.songByUrl[first.url],
    ]);
    expect(await store.hasAnyData(), isFalse);
  });

  test('account switch while reading claimed order prevents ordering and clearing guest data', () async {
    final store = GuestLibraryStore();
    await _guestAlbum(store);
    var owner = 'account';
    final api = _ClaimApi()..onMembershipRead = () => owner = 'other-account';

    expect(
      await _repository(
        store,
        api,
        owner: () => owner,
      ).syncGuestDataToCloud(userId: 'account'),
      isFalse,
    );
    expect(api.orderWrites, 0);
    expect(await store.hasAnyData(), isTrue);
  });

  test('concurrent account album edits reject stale ordering and retain guest data', () async {
    final store = GuestLibraryStore();
    final (album, _, _) = await _guestAlbum(store);
    final api = _ClaimApi();
    api.onMembershipRead = () {
      final id = api.albumsByKey[album.id]!;
      api.memberships[id]!.add('concurrent-account-song');
      api.versions[id] = api.versions[id]! + 1;
      api.onMembershipRead = null;
    };

    expect(
      await _repository(store, api).syncGuestDataToCloud(userId: 'account'),
      isFalse,
    );
    expect(api.orderWrites, 0);
    expect(
      api.memberships[api.albumsByKey[album.id]],
      contains('concurrent-account-song'),
    );
    expect(await store.hasAnyData(), isTrue);
  });

  test(
    'guest edits during server ordering preserve the changed local state',
    () async {
      final store = GuestLibraryStore();
      final (album, first, second) = await _guestAlbum(store);
      final api = _ClaimApi()
        ..onOrder = () =>
            store.orderAlbumSongs(album.id, [first.id, second.id]);

      expect(
        await _repository(store, api).syncGuestDataToCloud(userId: 'account'),
        isFalse,
      );
      expect(await store.hasAnyData(), isTrue);
      expect(
        (await store.listSongs(albumId: album.id)).items.map((s) => s.id),
        [first.id, second.id],
      );
    },
  );
}
