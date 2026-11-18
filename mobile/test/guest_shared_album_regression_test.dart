import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/core/storage/guest_library_store.dart';
import 'package:tmusic/features/library/library_repository.dart';

class _Guest extends SessionController {
  @override
  Future<Session?> build() async => null;
}

class _SharedAlbumApi extends ApiClient {
  int total = 201;
  bool failSecondPage = false;

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (method != 'GET' || path != '/shared-albums/MUSIC234') {
      throw StateError('Unexpected $method $path');
    }
    final page = query?['page'] as int? ?? 1;
    if (failSecondPage && page == 2) {
      throw const ApiFailure('network_error');
    }
    final start = (page - 1) * 200;
    final end = (start + 200).clamp(0, total);
    return {
      'shareCode': 'MUSIC234',
      'albumName': 'Album được chia sẻ',
      'sharedBy': 'Người tạo album',
      'songCount': total,
      'page': page,
      'pageSize': 200,
      'hasMore': end < total,
      'songs': [
        for (var index = start; index < end; index++)
          {
            'provider': 'youtube',
            'providerContentId': 'Video${index.toString().padLeft(6, '0')}',
            'canonicalUrl':
                'https://www.youtube.com/watch?v=Video${index.toString().padLeft(6, '0')}',
            'customTitle': 'Tên riêng $index',
            'originalTitle': 'Tên nguồn $index',
            'originalCreator': 'Kênh gốc',
          },
      ],
    };
  }
}

Future<LibraryRepository> _repo(ProviderContainer container) async {
  await container.read(sessionProvider.future);
  return container.read(libraryRepositoryProvider);
}

void main() {
  test(
    'guest song identity preserves case-sensitive YouTube content IDs',
    () async {
      final store = GuestLibraryStore();
      final first = await store.saveSong(
        url: 'https://www.youtube.com/watch?v=AbCdEfGh123',
        provider: 'youtube',
        providerContentId: 'AbCdEfGh123',
        title: 'Bài thứ nhất',
        albumIds: [],
      );
      final second = await store.saveSong(
        url: 'https://www.youtube.com/watch?v=abCdEfGh123',
        provider: 'youtube',
        providerContentId: 'abCdEfGh123',
        title: 'Bài thứ hai',
        albumIds: [],
      );

      expect(second.id, isNot(first.id));
      expect((await store.listSongs()).total, 2);
      expect(
        await store.findByCanonicalUrl(
          'https://www.youtube.com/watch?v=ABcDEfGH123',
        ),
        isNull,
      );
    },
  );

  test(
    'guest deduplication uses provider and ID across URL variants',
    () async {
      final store = GuestLibraryStore();
      final first = await store.saveSong(
        url: 'https://youtu.be/AbCdEfGh123',
        provider: 'youtube',
        providerContentId: 'AbCdEfGh123',
        title: 'Bài gốc',
        albumIds: ['album-a'],
      );
      final second = await store.saveSong(
        url: 'https://www.youtube.com/watch?v=AbCdEfGh123',
        provider: 'youtube',
        providerContentId: 'AbCdEfGh123',
        title: 'Tên khác',
        albumIds: ['album-b'],
      );

      expect(second.id, first.id);
      expect(second.albumIds, ['album-a', 'album-b']);
      expect(second.title, 'Bài gốc');
    },
  );

  test(
    'guest shared import fetches every page and preserves source metadata',
    () async {
      final store = GuestLibraryStore();
      final container = ProviderContainer(
        overrides: [
          sessionProvider.overrideWith(_Guest.new),
          guestLibraryStoreProvider.overrideWithValue(store),
          publicApiProvider.overrideWithValue(_SharedAlbumApi()),
        ],
      );
      addTearDown(container.dispose);

      final imported = await (await _repo(container))
          .importSharedAlbum(' music234 ');
      final songs = await store.listSongs(albumId: imported.id);

      expect(imported.songCount, 201);
      expect(songs.total, 201);
      expect(songs.hasMore, isFalse);
      expect(songs.items.first.title, 'Tên riêng 0');
      final first = songs.items.firstWhere(
        (song) => song.title == 'Tên riêng 0',
      );
      expect(first.providerContentId, 'Video000000');
      expect(first.originalTitle, 'Tên nguồn 0');
      expect(first.originalCreator, 'Kênh gốc');
    },
  );

  test('failed shared preview page creates no partial guest album', () async {
    final store = GuestLibraryStore();
    final api = _SharedAlbumApi()..failSecondPage = true;
    final container = ProviderContainer(
      overrides: [
        sessionProvider.overrideWith(_Guest.new),
        guestLibraryStoreProvider.overrideWithValue(store),
        publicApiProvider.overrideWithValue(api),
      ],
    );
    addTearDown(container.dispose);

    await expectLater(
      (await _repo(container)).importSharedAlbum('MUSIC234'),
      throwsA(isA<ApiFailure>()),
    );
    expect(await store.hasAnyData(), isFalse);
  });

  test(
    'oversized shared album is rejected before guest import writes',
    () async {
      final store = GuestLibraryStore();
      final api = _SharedAlbumApi()..total = 5001;
      final container = ProviderContainer(
        overrides: [
          sessionProvider.overrideWith(_Guest.new),
          guestLibraryStoreProvider.overrideWithValue(store),
          publicApiProvider.overrideWithValue(api),
        ],
      );
      addTearDown(container.dispose);

      await expectLater(
        (await _repo(container)).importSharedAlbum('MUSIC234'),
        throwsA(
          isA<ApiFailure>().having(
            (e) => e.code,
            'code',
            'SHARED_ALBUM_TOO_LARGE',
          ),
        ),
      );
      expect(await store.hasAnyData(), isFalse);
    },
  );
}
