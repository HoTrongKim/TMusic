import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/app/tmusic_app.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/features/settings/settings_controller.dart';

class _AccountSession extends SessionController {
  @override
  Future<Session?> build() async => Session(
    userId: 'theme-test-owner',
    accessToken: 'test-only',
    refreshToken: 'test-only',
    expiresAt: DateTime.now().add(const Duration(hours: 1)),
  );
}

class _ThemeApi extends ApiClient {
  bool failSave = false;
  bool showAccountSong = false;
  Completer<Map<String, dynamic>>? pendingSettings;
  Map<String, dynamic> settings = {'theme': 'dark', 'language': 'vi'};
  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (path == '/users/me/settings') {
      if (method == 'GET' && pendingSettings != null) {
        return pendingSettings!.future;
      }
      if (method == 'PATCH') {
        if (failSave) throw const ApiFailure('network_error');
        settings = {...?data};
      }
      return settings;
    }
    if (path == '/library/songs' && showAccountSong) {
      return {
        'items': [
          {
            'id': 'private-song',
            'version': 'v1',
            'customTitle': 'Bài riêng sau đăng nhập',
            'provider': 'soundcloud',
            'providerContentId': 'artist/private',
            'canonicalUrl': 'https://soundcloud.com/artist/private',
            'albumIds': <String>[],
          },
        ],
        'total': 1,
        'page': 1,
        'pageSize': 30,
      };
    }
    if (path == '/users/me') {
      return {
        'displayName': 'Người dùng',
        'authProviders': ['password'],
      };
    }
    return {'items': [], 'total': 0, 'page': 1, 'pageSize': 30};
  }
}

Future<ProviderContainer> _open(
  WidgetTester tester,
  _ThemeApi api, {
  bool account = false,
}) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        publicApiProvider.overrideWithValue(api),
        authorizedApiProvider.overrideWithValue(api),
        if (account) sessionProvider.overrideWith(_AccountSession.new),
      ],
      child: const TMusicApp(),
    ),
  );
  await tester.pumpAndSettle();
  return ProviderScope.containerOf(tester.element(find.byType(TMusicApp)));
}

void main() {
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));
  for (final account in [false, true]) {
    testWidgets(
      'Home light/dark toggle persists and Settings lives in Profile: account=$account',
      (tester) async {
        final api = _ThemeApi();
        final c = await _open(tester, api, account: account);
        expect(find.byKey(const Key('home-settings-shortcut')), findsNothing);
        final toggle = find.byKey(const Key('home-theme-toggle'));
        expect(toggle, findsOneWidget);
        expect(find.byTooltip('Chuyển sang giao diện sáng'), findsOneWidget);
        await tester.tap(toggle);
        await tester.pumpAndSettle();
        expect(c.read(settingsProvider).value?.theme, 'light');
        expect(Theme.of(tester.element(toggle)).brightness, Brightness.light);
        expect(find.byTooltip('Chuyển sang giao diện tối'), findsOneWidget);
        c.invalidate(settingsProvider);
        await tester.pumpAndSettle();
        expect(c.read(settingsProvider).value?.theme, 'light');
        await tester.tap(toggle);
        await tester.pumpAndSettle();
        expect(c.read(settingsProvider).value?.theme, 'dark');
        await tester.tap(find.byKey(const Key('nav-profile')));
        await tester.pumpAndSettle();
        final settings = find.byKey(const Key('profile-settings-shortcut'));
        await tester.ensureVisible(settings);
        await tester.tap(settings);
        await tester.pumpAndSettle();
        expect(find.text('Giao diện'), findsOneWidget);
      },
    );
  }

  testWidgets(
    'failed theme save keeps current appearance and reports failure',
    (tester) async {
      final api = _ThemeApi()..failSave = true;
      final c = await _open(tester, api, account: true);
      await tester.tap(find.byKey(const Key('home-theme-toggle')));
      await tester.pumpAndSettle();
      expect(c.read(settingsProvider).value?.theme, 'dark');
      expect(
        find.text('Không kết nối được. Kiểm tra mạng rồi thử lại.'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'Home waits for account preferences before changing only the theme',
    (tester) async {
      final api = _ThemeApi()
        ..pendingSettings = Completer<Map<String, dynamic>>();
      await _open(tester, api, account: true);
      final toggle = find.byKey(const Key('home-theme-toggle'));
      expect(tester.widget<IconButton>(toggle).onPressed, isNull);
      api.pendingSettings!.complete({'theme': 'dark', 'language': 'en'});
      await tester.pumpAndSettle();
      expect(tester.widget<IconButton>(toggle).onPressed, isNotNull);
      await tester.tap(toggle);
      await tester.pumpAndSettle();
      expect(api.settings['theme'], 'light');
      expect(api.settings['language'], 'en');
    },
  );

  testWidgets(
    'Home replaces guest data after sign-in and clears account content on logout',
    (tester) async {
      final api = _ThemeApi()..showAccountSong = true;
      final c = await _open(tester, api);
      expect(find.text('Bài riêng sau đăng nhập'), findsNothing);
      await c.read(sessionProvider.notifier).accept({
        'userId': 'new-owner',
        'accessToken': 'test-only',
        'refreshToken': 'test-only',
        'expiresAt': DateTime.now()
            .add(const Duration(hours: 1))
            .toIso8601String(),
      });
      await tester.pumpAndSettle();
      expect(find.text('Bài riêng sau đăng nhập'), findsWidgets);
      await c.read(sessionProvider.notifier).clear();
      await tester.pumpAndSettle();
      expect(find.text('Bài riêng sau đăng nhập'), findsNothing);
      expect(find.byKey(const Key('home-guest-login')), findsOneWidget);
    },
  );

  testWidgets('theme toggle fits 320dp with 200 percent text', (tester) async {
    await tester.binding.setSurfaceSize(const Size(320, 850));
    tester.platformDispatcher.textScaleFactorTestValue = 2;
    addTearDown(() {
      tester.binding.setSurfaceSize(null);
      tester.platformDispatcher.clearTextScaleFactorTestValue();
    });
    await _open(tester, _ThemeApi());
    final toggle = find.byKey(const Key('home-theme-toggle'));
    expect(toggle, findsOneWidget);
    await tester.tap(toggle);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });
}
