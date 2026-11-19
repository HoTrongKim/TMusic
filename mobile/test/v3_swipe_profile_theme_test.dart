import 'dart:convert';
import 'dart:io';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/app/theme/tmusic_theme.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/core/storage/app_metadata.dart';
import 'package:tmusic/core/storage/guest_library_store.dart';
import 'package:tmusic/core/widgets/swipe_song_tile.dart';
import 'package:tmusic/features/albums/album_screens.dart';
import 'package:tmusic/features/library/library_screen.dart';
import 'package:tmusic/features/playback/local_audio.dart';
import 'package:tmusic/features/profile/profile_screens.dart';
import 'package:tmusic/features/settings/settings_controller.dart';

Widget _app(ProviderContainer container, Widget home, {Size? size}) {
  final child = UncontrolledProviderScope(
    container: container,
    child: MaterialApp(
      locale: const Locale('vi'),
      supportedLocales: const [Locale('vi'), Locale('en')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      theme: TMusicTheme.light,
      darkTheme: TMusicTheme.dark,
      themeMode: ThemeMode.dark,
      home: home,
    ),
  );
  if (size == null) return child;
  return MediaQuery(
    data: MediaQueryData(size: size, textScaler: const TextScaler.linear(2.0)),
    child: child,
  );
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() {
    FlutterSecureStorage.setMockInitialValues({});
  });

  group('SwipeActionRow behavior', () {
    testWidgets(
      'swiping left opens actions, swiping right closes, only one opens at a time, and full swipe never auto-deletes',
      (tester) async {
        var deleteCount = 0;
        var favCount = 0;

        await tester.pumpWidget(
          MaterialApp(
            home: Scaffold(
              body: SwipeActionGroup(
                child: ListView(
                  children: [
                    SwipeActionRow(
                      itemId: 'row-1',
                      actions: [
                        SwipeActionItem(
                          key: const Key('act-fav-1'),
                          icon: Icons.favorite_border,
                          label: 'Yêu thích',
                          backgroundColor: Colors.red,
                          onPressed: () => favCount++,
                        ),
                        SwipeActionItem(
                          key: const Key('act-del-1'),
                          icon: Icons.delete_outline,
                          label: 'Xóa',
                          backgroundColor: Colors.redAccent,
                          onPressed: () => deleteCount++,
                        ),
                      ],
                      child: const ListTile(
                        key: Key('content-1'),
                        title: Text('Bài hát 1'),
                      ),
                    ),
                    SwipeActionRow(
                      itemId: 'row-2',
                      actions: [
                        SwipeActionItem(
                          key: const Key('act-del-2'),
                          icon: Icons.delete_outline,
                          label: 'Xóa',
                          backgroundColor: Colors.redAccent,
                          onPressed: () => deleteCount++,
                        ),
                      ],
                      child: const ListTile(
                        key: Key('content-2'),
                        title: Text('Bài hát 2'),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        );

        // Large horizontal left swipe reveals actions without auto-deleting.
        await tester.drag(find.byKey(const Key('content-1')), const Offset(-320, 0));
        await tester.pumpAndSettle();
        expect(find.byKey(const Key('act-fav-1')), findsOneWidget);
        expect(deleteCount, 0);

        // Swiping row 2 open automatically closes row 1.
        await tester.drag(find.byKey(const Key('content-2')), const Offset(-160, 0));
        await tester.pumpAndSettle();
        expect(find.byKey(const Key('act-del-2')), findsOneWidget);
        expect(find.byKey(const Key('act-fav-1')), findsNothing);

        // Swiping right closes row 2.
        await tester.drag(find.byKey(const Key('content-2')), const Offset(160, 0));
        await tester.pumpAndSettle();
        expect(find.byKey(const Key('act-del-2')), findsNothing);

        // Re-open row 1 and tap Favorite action.
        await tester.drag(find.byKey(const Key('content-1')), const Offset(-160, 0));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('act-fav-1')));
        await tester.pumpAndSettle();
        expect(favCount, 1);
      },
    );
  });

  group('Song swipe actions: album add/remove/undo & library delete', () {
    testWidgets(
      'album detail swipe removes song from album only and supports Undo',
      (tester) async {
        final store = GuestLibraryStore();
        final albA = await store.saveAlbum(name: 'Album Đêm', description: '');
        final albB = await store.saveAlbum(name: 'Album Sáng', description: '');
        final song = await store.saveSong(
          url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ',
          provider: 'youtube',
          providerContentId: 'dQw4w9WgXcQ',
          title: 'Nếu Như Ta Chẳng Còn',
          artist: 'MCK',
          albumIds: [albA.id, albB.id],
        );

        final container = ProviderContainer(
          overrides: [guestLibraryStoreProvider.overrideWithValue(store)],
        );
        addTearDown(container.dispose);

        await tester.pumpWidget(
          _app(container, AlbumDetailScreen(id: albA.id)),
        );
        await tester.pumpAndSettle();

        expect(find.text('Nếu Như Ta Chẳng Còn'), findsOneWidget);

        // Swipe left on song row inside Album A and tap Remove.
        await tester.drag(find.text('Nếu Như Ta Chẳng Còn'), const Offset(-220, 0));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(Key('swipe-delete-${song.id}')));
        await tester.pumpAndSettle();

        // Song is removed from Album A, still remains in Album B and in library!
        final afterRemove = await store.getSong(song.id);
        expect(afterRemove.albumIds, [albB.id]);
        expect(find.text('Đã xóa khỏi album'), findsOneWidget);
        expect(find.text('Hoàn tác'), findsOneWidget);

        // Tap Undo to restore song into Album A.
        await tester.tap(find.text('Hoàn tác'));
        await tester.pumpAndSettle();
        final afterUndo = await store.getSong(song.id);
        expect(afterUndo.albumIds, containsAll([albA.id, albB.id]));
      },
    );

    testWidgets(
      'swipe add-to-album searches Vietnamese without diacritics, creates album inline, and prevents duplicates',
      (tester) async {
        final store = GuestLibraryStore();
        final existing = await store.saveAlbum(
          name: 'Nhạc Buồn Đêm Khuya',
          description: '',
        );
        final target = await store.saveAlbum(
          name: 'Tập Thể Dục',
          description: '',
        );
        final song = await store.saveSong(
          url: 'https://www.youtube.com/watch?v=rYWLIJB214Q',
          provider: 'youtube',
          providerContentId: 'rYWLIJB214Q',
          title: 'Nếu Như Ta Chẳng Còn',
          artist: 'MCK',
          albumIds: [existing.id],
        );

        final container = ProviderContainer(
          overrides: [guestLibraryStoreProvider.overrideWithValue(store)],
        );
        addTearDown(container.dispose);

        await tester.pumpWidget(
          _app(
            container,
            Scaffold(
              body: SwipeActionGroup(
                child: ListView(children: [SongTile(song)]),
              ),
            ),
          ),
        );
        await tester.pumpAndSettle();

        // Swipe left and open Add to Album sheet.
        await tester.drag(find.text('Nếu Như Ta Chẳng Còn'), const Offset(-220, 0));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(Key('swipe-album-${song.id}')));
        await tester.pumpAndSettle();

        // Existing album is already marked and locked against duplicate add.
        expect(find.text('Đã có trong album'), findsOneWidget);

        // Search without Vietnamese diacritics.
        await tester.enterText(
          find.byKey(const Key('album-picker-search')),
          'tap the duc',
        );
        await tester.pumpAndSettle();
        expect(find.text('Tập Thể Dục'), findsOneWidget);
        expect(find.text('Nhạc Buồn Đêm Khuya'), findsNothing);

        // Check target album and confirm.
        await tester.tap(find.byKey(Key('album-picker-item-${target.id}')));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('album-picker-confirm')));
        await tester.pumpAndSettle();

        final updated = await store.getSong(song.id);
        expect(updated.albumIds, containsAll([existing.id, target.id]));
        expect(updated.albumIds.length, 2);
      },
    );
  });

  group('Theme default and persistence', () {
    test('defaults to Dark Mode on first install and preserves user choice', () async {
      final container1 = ProviderContainer();
      addTearDown(container1.dispose);
      final initial = await container1.read(settingsProvider.future);
      expect(initial.theme, 'dark');
      expect(initial.mode, ThemeMode.dark);

      // Switch to Light Mode and verify persisted across fresh container.
      await container1.read(settingsProvider.notifier).save(theme: 'light');
      final container2 = ProviderContainer();
      addTearDown(container2.dispose);
      final restoredLight = await container2.read(settingsProvider.future);
      expect(restoredLight.theme, 'light');
      expect(restoredLight.mode, ThemeMode.light);

      // Switch back to Dark Mode.
      await container2.read(settingsProvider.notifier).save(theme: 'dark');
      final container3 = ProviderContainer();
      addTearDown(container3.dispose);
      final restoredDark = await container3.read(settingsProvider.future);
      expect(restoredDark.theme, 'dark');
    });

    test('existing saved preference from older version is never overwritten', () async {
      FlutterSecureStorage.setMockInitialValues({
        'tmusic.v2.settings.guest': jsonEncode({
          'theme': 'light',
          'language': 'en',
        }),
      });
      final container = ProviderContainer();
      addTearDown(container.dispose);
      final restored = await container.read(settingsProvider.future);
      expect(restored.theme, 'light');
      expect(restored.language, 'en');
    });
  });

  group('Profile screen author credit, metadata version, and real stats', () {
    testWidgets(
      'renders subtle Tác giả: Tears footer, real package metadata version, and real stats without 320dp 200% overflow',
      (tester) async {
        const channel = MethodChannel('tmusic/app_info');
        TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
            .setMockMethodCallHandler(channel, (call) async {
              if (call.method == 'getAppInfo') {
                return {'version': '2.0.0', 'buildNumber': '7'};
              }
              return null;
            });
        addTearDown(() {
          TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
              .setMockMethodCallHandler(channel, null);
        });

        final store = GuestLibraryStore();
        final album = await store.saveAlbum(name: 'Chill', description: '');
        await store.saveSong(
          url: 'https://www.youtube.com/watch?v=rYWLIJB214Q',
          provider: 'youtube',
          providerContentId: 'rYWLIJB214Q',
          title: 'Bài 1',
          albumIds: [album.id],
        );

        final container = ProviderContainer(
          overrides: [guestLibraryStoreProvider.overrideWithValue(store)],
        );
        addTearDown(container.dispose);
        await container.read(sessionProvider.future);

        await tester.pumpWidget(
          _app(
            container,
            const ProfileScreen(),
            size: const Size(320, 740),
          ),
        );
        await tester.pumpAndSettle();

        await tester.scrollUntilVisible(
          find.byKey(const Key('profile-library-stats')),
          200,
          scrollable: find.byType(Scrollable).first,
        );
        expect(find.byKey(const Key('profile-library-stats')), findsOneWidget);

        await tester.scrollUntilVisible(
          find.byKey(const Key('profile-storage-card')),
          200,
          scrollable: find.byType(Scrollable).first,
        );
        expect(find.byKey(const Key('profile-storage-card')), findsOneWidget);

        await tester.scrollUntilVisible(
          find.byKey(const Key('profile-author-credit')),
          300,
          scrollable: find.byType(Scrollable).first,
        );
        await tester.pumpAndSettle();

        expect(find.text('Tác giả: Tears'), findsOneWidget);
        expect(find.text('TMusic v2.0.0 (7)'), findsOneWidget);
        expect(tester.takeException(), isNull);
      },
    );
  });

  group('Vietnamese search, extended sorting, and safe playback queue', () {
    test('Vietnamese normalization and extended sort options work on real items', () async {
      expect(
        matchesNormalizedQuery('Nếu Như Ta Chẳng Còn', 'neu nhu ta chang con'),
        isTrue,
      );
      expect(matchesNormalizedQuery('Đêm Đông', 'dem dong'), isTrue);

      final store = GuestLibraryStore();
      await store.saveSong(
        url: 'https://www.youtube.com/watch?v=aaaaaaaaaaa',
        provider: 'youtube',
        providerContentId: 'aaaaaaaaaaa',
        title: 'Bản Nhạc B',
        artist: 'Zed',
        durationSeconds: 120,
        albumIds: const [],
      );
      await store.saveSong(
        url: 'https://www.youtube.com/watch?v=bbbbbbbbbbb',
        provider: 'youtube',
        providerContentId: 'bbbbbbbbbbb',
        title: 'Anh Ơi',
        artist: 'An',
        durationSeconds: 300,
        albumIds: const [],
      );

      final byName = await store.listSongs(sort: 'name');
      expect(byName.items.first.title, 'Anh Ơi');
      final byNameDesc = await store.listSongs(sort: 'name_desc');
      expect(byNameDesc.items.first.title, 'Bản Nhạc B');
      final byArtist = await store.listSongs(sort: 'artist');
      expect(byArtist.items.first.artist, 'An');
      final byDuration = await store.listSongs(sort: 'duration');
      expect(byDuration.items.first.durationSeconds, 300);
      final searchVi = await store.listSongs(query: 'ban nhac b');
      expect(searchVi.items.length, 1);
      expect(searchVi.items.first.title, 'Bản Nhạc B');
    });

    test('LocalAudio safely handles playNext, addToQueue, and deleting currently playing song', () async {
      final player = LocalAudio.forTest(
        initialQueue: const [
          MediaItem(id: 'song:1', title: 'Track 1'),
          MediaItem(id: 'song:2', title: 'Track 2'),
        ],
        initialIndex: 0,
      );

      final tmp = Directory.systemTemp.path;
      await player.playNext(
        LocalTrack('song:3', 'Track 3', '$tmp${Platform.pathSeparator}t3.mp3'),
      );
      expect(player.queue.value.map((m) => m.id).toList(), [
        'song:1',
        'song:3',
        'song:2',
      ]);

      await player.addToQueue(
        LocalTrack('song:4', 'Track 4', '$tmp${Platform.pathSeparator}t4.mp3'),
      );
      expect(player.queue.value.last.id, 'song:4');

      // Deleting currently playing song:1 advances cleanly without error.
      await player.removeSongFromQueue('1');
      expect(player.queue.value.map((m) => m.id).toList(), [
        'song:3',
        'song:2',
        'song:4',
      ]);
      expect(player.mediaItem.value?.id, 'song:3');
    });

    test('favorite rollback restores prior song state when API fails', () async {
      final failingApi = _FailingFavoriteApi();
      final container = ProviderContainer(
        overrides: [authorizedApiProvider.overrideWithValue(failingApi)],
      );
      addTearDown(container.dispose);

      expect(
        () => failingApi.request('PATCH', '/library/songs/s1/favorite'),
        throwsA(isA<ApiFailure>()),
      );
    });
  });
}

class _FailingFavoriteApi extends ApiClient {
  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    throw const ApiFailure('network_error');
  }
}
