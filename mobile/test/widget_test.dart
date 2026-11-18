import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/app/tmusic_app.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/core/storage/guest_library_store.dart';
import 'package:tmusic/features/settings/settings_controller.dart';
import 'package:tmusic/features/share/shared_links.dart';

class TestSession extends SessionController {
  @override
  Future<Session?> build() async => Session.fromJson({
    'accessToken': 'test-only',
    'refreshToken': 'test-only',
    'userId': 'fixture-user',
    'expiresAt': DateTime.now().add(const Duration(hours: 1)).toIso8601String(),
  });
}

class FakeBackendApi extends ApiClient {
  FakeBackendApi({this.fail = false});
  bool fail;
  final List<Map<String, dynamic>> albums = [];
  final List<Map<String, dynamic>> songs = [];
  final requests = <String>[];

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    requests.add('$method $path');
    if (fail) throw const ApiFailure('network_error');

    if ((path == '/auth/login' || path == '/auth/register') &&
        method == 'POST') {
      return {
        'accessToken': 'logged-in-token',
        'refreshToken': 'logged-in-refresh',
        'userId': 'user-1',
        'expiresAt': DateTime.now()
            .add(const Duration(minutes: 10))
            .toIso8601String(),
      };
    }

    if (path == '/users/me' && method == 'GET') {
      return {
        'id': 'fixture-user',
        'email': 'test@example.test',
        'displayName': 'Người dùng TMusic',
        'emailVerified': true,
        'authProviders': ['password'],
      };
    }

    if (path == '/albums' && method == 'GET') {
      final fav = query?['favorite'];
      final filtered = fav == true
          ? albums.where((a) => a['isFavorite'] == true).toList()
          : albums;
      return {
        'items': filtered,
        'total': filtered.length,
        'page': 1,
        'pageSize': 30,
      };
    }

    if (path == '/albums' && method == 'POST') {
      final created = {
        'id': 'album-${albums.length + 1}',
        'name': data?['name'] ?? 'Album mới',
        'description': data?['description'],
        'coverImageUrl': data?['coverImageUrl'],
        'isFavorite': data?['isFavorite'] ?? false,
        'songCount': 0,
        'sortOrder': 0,
        'version': 'v1',
      };
      albums.add(created);
      return created;
    }

    if (path.startsWith('/albums/') && method == 'GET') {
      final id = path.split('/')[2];
      final found = albums.firstWhere(
        (a) => a['id'] == id,
        orElse: () => {
          'id': id,
          'name': 'Album kiểm thử',
          'version': 'v1',
          'songCount': songs.length,
        },
      );
      if (path.endsWith('/songs')) {
        return {
          'items': songs,
          'total': songs.length,
          'page': 1,
          'pageSize': 30,
        };
      }
      return found;
    }

    if (path == '/songs/resolve' && method == 'POST') {
      final url = data?['url'] as String? ?? '';
      final isTikTok = url.contains('tiktok.com');
      return {
        'provider': isTikTok ? 'tiktok' : 'youtube',
        'providerContentId': isTikTok ? '1234567890' : 'AbCdEfGh123',
        'canonicalUrl': url,
        'originalTitle': isTikTok ? 'Giai điệu TikTok' : 'Bản nhạc YouTube gốc',
        'originalCreator': 'Kênh Nghệ Sĩ',
        'durationSeconds': 215,
        'playbackCapability': 'EMBED_SUPPORTED',
        'existingLibrarySongId': null,
        'existingSong': null,
      };
    }

    if (path == '/library/songs' && method == 'POST') {
      final saved = {
        'id': 'song-${songs.length + 1}',
        'version': 'v1',
        'customTitle': data?['customTitle'] ?? '',
        'customArtist': data?['customArtist'] ?? '',
        'provider': (data?['url'] as String? ?? '').contains('tiktok')
            ? 'tiktok'
            : 'youtube',
        'providerContentId': 'AbCdEfGh123',
        'canonicalUrl':
            data?['url'] ?? 'https://www.youtube.com/watch?v=AbCdEfGh123',
        'originalTitle': 'Bản nhạc YouTube gốc',
        'originalCreator': 'Kênh Nghệ Sĩ',
        'playbackCapability': 'EMBED_SUPPORTED',
        'isFavorite': false,
        'albumIds': data?['albumIds'] ?? <String>[],
      };
      songs.add(saved);
      return saved;
    }

    if (path == '/library/songs' && method == 'GET') {
      return {'items': songs, 'total': songs.length, 'page': 1, 'pageSize': 30};
    }

    if (path == '/search' && method == 'GET') {
      return {
        'albums': {
          'items': albums,
          'total': albums.length,
          'page': 1,
          'pageSize': 30,
        },
        'songs': {
          'items': songs,
          'total': songs.length,
          'page': 1,
          'pageSize': 30,
        },
      };
    }

    return {'items': <dynamic>[], 'total': 0, 'page': 1, 'pageSize': 30};
  }
}

class TestSettings extends SettingsController {
  @override
  Future<UserSettings> build() async => const UserSettings();
}

void main() {
  test('share parser enforces HTTPS and the three provider allowlists', () {
    expect(
      SharedLinks.parse(
        'https://youtube.com.evil.test/watch?v=abc https://127.0.0.1/x https://youtu.be/abcdefghijk https://vt.tiktok.com/ZS12345/',
      ),
      ['https://youtu.be/abcdefghijk', 'https://vt.tiktok.com/ZS12345/'],
    );
    expect(
      SharedLinks.supported('https://user@youtube.com/watch?v=abcdefghijk'),
      isFalse,
    );
    expect(
      SharedLinks.supported('https://www.tiktok.com:444/@x/video/123'),
      isFalse,
    );
    expect(
      SharedLinks.parse(
        'https://soundcloud.com/artist/track '
        'https://on.soundcloud.com/AbCdEf https://soundcloud.com/discover/sets',
      ),
      [
        'https://soundcloud.com/artist/track',
        'https://on.soundcloud.com/AbCdEf',
      ],
    );
  });

  testWidgets('security route requires authentication in guest mode', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    await tester.pumpWidget(
      const ProviderScope(child: TMusicApp(initialLocation: '/security')),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('auth-submit')), findsOneWidget);
    expect(find.byKey(const Key('nav-albums')), findsNothing);
    expect(find.byKey(const Key('nav-add')), findsNothing);
  });

  testWidgets(
    'guest creates an album, changes settings locally and syncs on login',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      await tester.binding.setSurfaceSize(const Size(412, 915));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final api = FakeBackendApi();
      final store = GuestLibraryStore();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            publicApiProvider.overrideWithValue(api),
            authorizedApiProvider.overrideWithValue(api),
            guestLibraryStoreProvider.overrideWithValue(store),
          ],
          child: const TMusicApp(initialLocation: '/welcome'),
        ),
      );
      await tester.pumpAndSettle();
      final guest = find.byKey(const Key('welcome-guest'));
      await tester.ensureVisible(guest);
      await tester.tap(guest);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('nav-albums')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('library-create-album')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('album-name')),
        'Nhạc trên máy',
      );
      final save = find.byKey(const Key('album-save'));
      await tester.ensureVisible(save);
      await tester.tap(save);
      await tester.pumpAndSettle();
      expect(find.text('Nhạc trên máy'), findsOneWidget);
      expect(api.albums, isEmpty);

      await tester.tap(find.byKey(const Key('nav-profile')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('profile-guest-register')), findsOneWidget);
      expect(find.text('Phiên đăng nhập'), findsNothing);
      final container = ProviderScope.containerOf(
        tester.element(find.byType(TMusicApp).first),
      );
      await container.read(settingsProvider.notifier).save(theme: 'light');
      expect(container.read(settingsProvider).value?.theme, 'light');
      await tester.pumpAndSettle();
      expect(api.requests.where((r) => r.contains('/users/me')), isEmpty);

      await tester.tap(find.byKey(const Key('profile-guest-login')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('auth-email')),
        'user@tmusic.test',
      );
      await tester.enterText(
        find.byKey(const Key('auth-password')),
        'StrongPass!2026',
      );
      await tester.tap(find.byKey(const Key('auth-submit')));
      await tester.pumpAndSettle();
      expect(api.albums.single['name'], 'Nhạc trên máy');
      expect(await store.hasAnyData(), isFalse);
    },
  );

  testWidgets('login validates email and completes login flow to Home', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    final fakeApi = FakeBackendApi();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          publicApiProvider.overrideWithValue(fakeApi),
          authorizedApiProvider.overrideWithValue(fakeApi),
          settingsProvider.overrideWith(TestSettings.new),
        ],
        child: const TMusicApp(initialLocation: '/auth/login'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('auth-submit')));
    await tester.pumpAndSettle();
    expect(find.text('Nhập địa chỉ email hợp lệ.'), findsOneWidget);
    expect(find.text('Vui lòng điền thông tin này.'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('auth-email')),
      'user@tmusic.test',
    );
    await tester.enterText(
      find.byKey(const Key('auth-password')),
      'StrongPass!2026',
    );
    await tester.tap(find.byKey(const Key('auth-submit')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-add')), findsOneWidget);
  });

  testWidgets(
    'register signs in immediately without requiring email verification',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      final fakeApi = FakeBackendApi();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            publicApiProvider.overrideWithValue(fakeApi),
            authorizedApiProvider.overrideWithValue(fakeApi),
            settingsProvider.overrideWith(TestSettings.new),
          ],
          child: const TMusicApp(initialLocation: '/auth/register'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('auth-code')), findsNothing);

      await tester.enterText(find.byKey(const Key('auth-name')), 'Kim');
      await tester.enterText(
        find.byKey(const Key('auth-email')),
        'new@tmusic.test',
      );
      await tester.enterText(
        find.byKey(const Key('auth-password')),
        'StrongPass!2026',
      );
      await tester.ensureVisible(find.byKey(const Key('auth-submit')));
      await tester.tap(find.byKey(const Key('auth-submit')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('home-add')), findsOneWidget);
    },
  );

  testWidgets(
    'album creation flow and Smart Download Home Golden Path work end-to-end in UI',
    (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      await tester.binding.setSurfaceSize(const Size(412, 915));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final fakeApi = FakeBackendApi();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sessionProvider.overrideWith(TestSession.new),
            publicApiProvider.overrideWithValue(fakeApi),
            authorizedApiProvider.overrideWithValue(fakeApi),
            settingsProvider.overrideWith(TestSettings.new),
          ],
          child: const TMusicApp(),
        ),
      );
      await tester.pumpAndSettle();

      // Verify legacy Add tab is gone and new Downloads tab exists
      expect(find.byKey(const Key('nav-add')), findsNothing);
      expect(find.byKey(const Key('nav-downloads')), findsOneWidget);

      // Navigate to Albums tab -> Create Album
      await tester.tap(find.byKey(const Key('nav-albums')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('library-create-album')));
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(const Key('album-name')),
        'Nhạc Đêm Khuya',
      );
      await tester.ensureVisible(find.byKey(const Key('album-save')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('album-save')));
      await tester.pumpAndSettle();
      expect(find.text('Nhạc Đêm Khuya'), findsOneWidget);

      // Switch directly to Smart Download Home -> Enter URL -> Detect -> Select Album -> Save
      await tester.tap(find.byKey(const Key('nav-home')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('link-paste')), findsNothing);
      expect(find.text('Dán từ bộ nhớ tạm'), findsNothing);

      // Test unsupported URL rejection first (TEST 09: no fake metadata)
      await tester.enterText(
        find.byKey(const Key('link-url')),
        'https://evil.example/fake.mp3',
      );
      await tester.tap(find.byKey(const Key('link-detect')));
      await tester.pumpAndSettle();
      expect(
        find.text(
          'Liên kết không hợp lệ. Hãy nhập URL từ YouTube, TikTok hoặc SoundCloud.',
        ),
        findsOneWidget,
      );
      expect(find.byKey(const Key('link-save')), findsNothing);

      // Enter valid YouTube URL -> Detect -> Expand Rename -> Select Album -> Save
      await tester.enterText(
        find.byKey(const Key('link-url')),
        'https://youtu.be/AbCdEfGh123',
      );
      await tester.tap(find.byKey(const Key('link-detect')));
      await tester.pumpAndSettle();

      expect(find.text('Bản nhạc YouTube gốc'), findsWidgets);
      await tester.tap(find.text('Đổi tên bài hát / nghệ sĩ'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('link-title')),
        'Nhạc chill mỗi tối',
      );

      final albumChip = find.widgetWithText(
        ChoiceChip,
        'Gần nhất: Nhạc Đêm Khuya',
      );
      await tester.ensureVisible(albumChip);
      await tester.tap(albumChip);
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('link-save')));
      await tester.tap(find.byKey(const Key('link-save')));
      await tester.pumpAndSettle();

      expect(find.text('Đã lưu vào thư viện.'), findsOneWidget);
      expect(find.byKey(const Key('open-saved-album')), findsOneWidget);
      expect(find.text('Xem file đã lưu'), findsOneWidget);
    },
  );

  for (final width in [320.0, 430.0]) {
    for (final brightness in Brightness.values) {
      testWidgets(
        'authenticated four tabs $width ${brightness.name} 200% text',
        (tester) async {
          FlutterSecureStorage.setMockInitialValues({});
          await tester.binding.setSurfaceSize(Size(width, 850));
          tester.platformDispatcher.textScaleFactorTestValue = 2;
          tester.platformDispatcher.platformBrightnessTestValue = brightness;
          addTearDown(() {
            tester.binding.setSurfaceSize(null);
            tester.platformDispatcher.clearTextScaleFactorTestValue();
            tester.platformDispatcher.clearPlatformBrightnessTestValue();
          });
          await tester.pumpWidget(
            ProviderScope(
              overrides: [
                sessionProvider.overrideWith(TestSession.new),
                publicApiProvider.overrideWithValue(FakeBackendApi()),
                authorizedApiProvider.overrideWithValue(FakeBackendApi()),
                settingsProvider.overrideWith(TestSettings.new),
              ],
              child: const TMusicApp(),
            ),
          );
          await tester.pumpAndSettle();
          for (final tab in ['home', 'albums', 'downloads', 'profile']) {
            await tester.tap(find.byKey(Key('nav-$tab')));
            await tester.pumpAndSettle();
            expect(tester.takeException(), isNull, reason: tab);
          }
        },
      );
      testWidgets('auth screens $width ${brightness.name} 200% text', (
        tester,
      ) async {
        FlutterSecureStorage.setMockInitialValues({});
        await tester.binding.setSurfaceSize(Size(width, 850));
        tester.platformDispatcher.textScaleFactorTestValue = 2;
        tester.platformDispatcher.platformBrightnessTestValue = brightness;
        addTearDown(() {
          tester.binding.setSurfaceSize(null);
          tester.platformDispatcher.clearTextScaleFactorTestValue();
          tester.platformDispatcher.clearPlatformBrightnessTestValue();
        });
        await tester.pumpWidget(
          const ProviderScope(
            child: TMusicApp(initialLocation: '/auth/register'),
          ),
        );
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull, reason: 'layout');
      });
    }
  }

  testWidgets('network failure offers a retry and no fake library data', (
    tester,
  ) async {
    FlutterSecureStorage.setMockInitialValues({});
    await tester.binding.setSurfaceSize(const Size(390, 844));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final api = FakeBackendApi(fail: true);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(TestSession.new),
          authorizedApiProvider.overrideWithValue(api),
          settingsProvider.overrideWith(TestSettings.new),
        ],
        child: const TMusicApp(),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Thử lại'), findsOneWidget);
    expect(
      find.text('Không kết nối được. Kiểm tra mạng rồi thử lại.'),
      findsOneWidget,
    );

    // Recover from network failure when Retry is tapped
    api.fail = false;
    await tester.tap(find.text('Thử lại'));
    await tester.pumpAndSettle();
    expect(find.text('Thư viện đang chờ bài đầu tiên'), findsOneWidget);
  });
}
