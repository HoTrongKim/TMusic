import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:google_sign_in/google_sign_in.dart'
    show GoogleSignInException, GoogleSignInExceptionCode;
import 'package:tmusic/app/tmusic_app.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/external_auth.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/core/widgets/common.dart';
import 'package:tmusic/features/authentication/auth_screens.dart';
import 'package:tmusic/features/home/home_screen.dart';
import 'package:tmusic/features/settings/settings_controller.dart';

Map<String, dynamic> _session(String user) => {
  'userId': user,
  'accessToken': 'test-access-$user',
  'refreshToken': 'test-refresh-$user',
  'expiresAt': '2099-01-01T00:00:00.000Z',
};

class _ControlledGoogle extends GoogleIdentityClient {
  Completer<({String? idToken, String? displayName})> result = Completer();

  @override
  bool get isSupported => true;

  @override
  Future<({String? idToken, String? displayName})> authenticate() =>
      result.future;

  void succeed() => result.complete((
    idToken: 'test-google-id-token',
    displayName: 'Người dùng kiểm thử',
  ));
}

class _ControlledApi extends ApiClient {
  final response = Completer<Map<String, dynamic>>();

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (path == '/system/capabilities') {
      return {'googleSignIn': true, 'appleSignIn': false};
    }
    if (path == '/auth/google') {
      if (method != 'POST' || data?['idToken'] != 'test-google-id-token') {
        throw StateError('Google ID token must be verified by the backend');
      }
      return response.future;
    }
    if (path == '/auth/login' || path == '/auth/register') {
      if (method != 'POST' ||
          data?['email'] != 'account@example.test' ||
          data?['password'] != 'Fixture-password!2026') {
        throw StateError('Unexpected password authentication payload');
      }
      return response.future;
    }
    if (path == '/users/me') {
      return {
        'id': 'google-user',
        'email': 'google@example.test',
        'displayName': 'Người dùng kiểm thử',
        'emailVerified': true,
        'authProviders': ['google'],
      };
    }
    return {'items': <dynamic>[], 'total': 0, 'page': 1, 'pageSize': 30};
  }
}

class _Settings extends SettingsController {
  _Settings(this.value);
  final UserSettings value;
  @override
  Future<UserSettings> build() async => value;
}

class _DelayedStorage extends FlutterSecureStorage {
  _DelayedStorage({this.fail = false});
  final bool fail;
  final sessionWriteStarted = Completer<void>();
  final releaseWrite = Completer<void>();

  @override
  Future<void> write({
    required String key,
    required String? value,
    AppleOptions? iOptions,
    AndroidOptions? aOptions,
    LinuxOptions? lOptions,
    WebOptions? webOptions,
    AppleOptions? mOptions,
    WindowsOptions? wOptions,
  }) async {
    if (key == SessionController.storageKey && !releaseWrite.isCompleted) {
      if (!sessionWriteStarted.isCompleted) sessionWriteStarted.complete();
      await releaseWrite.future;
    }
    if (fail) throw PlatformException(code: 'test-storage-unavailable');
    await super.write(key: key, value: value);
  }
}

Future<ProviderContainer> _pumpApp(
  WidgetTester tester, {
  _ControlledGoogle? google,
  _ControlledApi? api,
  FlutterSecureStorage? storage,
  String initialLocation = '/auth/login',
  UserSettings settings = const UserSettings(),
}) async {
  final backend = api ?? _ControlledApi();
  addTearDown(backend.close);
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        googleIdentityProvider.overrideWithValue(google ?? _ControlledGoogle()),
        publicApiProvider.overrideWithValue(backend),
        authorizedApiProvider.overrideWithValue(backend),
        settingsProvider.overrideWith(() => _Settings(settings)),
        if (storage != null) secureStorageProvider.overrideWithValue(storage),
      ],
      child: TMusicApp(initialLocation: initialLocation),
    ),
  );
  await tester.pumpAndSettle();
  return ProviderScope.containerOf(tester.element(find.byType(TMusicApp)));
}

Future<void> _tapGoogle(WidgetTester tester) async {
  final google = find.byKey(const Key('auth-google'));
  await tester.ensureVisible(google);
  await tester.tap(google);
  await tester.pump();
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  test('superseded external session reports unsuccessful acceptance', () async {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    await container.read(sessionProvider.future);
    final controller = container.read(sessionProvider.notifier);
    final older = controller.beginAuthentication();
    final newer = controller.beginAuthentication();
    expect(await newer(_session('new')), isTrue);
    expect(await older(_session('old')), isFalse);
    expect(container.read(sessionProvider).value?.userId, 'new');
  });

  test('an attempt superseded during persistence never commits or survives restart', () async {
    final storage = _DelayedStorage();
    final container = ProviderContainer(
      overrides: [secureStorageProvider.overrideWithValue(storage)],
    );
    addTearDown(container.dispose);
    await container.read(sessionProvider.future);
    final controller = container.read(sessionProvider.notifier);
    final older = controller.beginAuthentication();
    final accepting = older(_session('old'));
    await storage.sessionWriteStarted.future;
    controller.beginAuthentication();
    storage.releaseWrite.complete();
    expect(await accepting, isFalse);
    expect(container.read(sessionProvider).value, isNull);
    expect(await storage.read(key: SessionController.storageKey), isNull);
  });

  testWidgets('login offers a clearly secondary registration action', (
    tester,
  ) async {
    await tester.pumpWidget(
      const ProviderScope(child: TMusicApp(initialLocation: '/auth/login')),
    );
    await tester.pumpAndSettle();
    expect(find.byType(AuthScreen), findsOneWidget);
    final register = find.widgetWithText(TextButton, 'Tạo tài khoản');
    expect(register, findsOneWidget);
    final formTop = tester.getTopLeft(find.byType(Form)).dy;
    expect(formTop, greaterThan(180));
    expect(tester.takeException(), isNull);
  });

  testWidgets('public Profile restore does not become the post-login target', (
    tester,
  ) async {
    final google = _ControlledGoogle();
    final api = _ControlledApi();
    await _pumpApp(
      tester,
      initialLocation: '/profile',
      google: google,
      api: api,
    );
    await tester.tap(find.byKey(const Key('profile-guest-login')));
    await tester.pumpAndSettle();
    expect(find.byType(AuthScreen), findsOneWidget);
    await _tapGoogle(tester);
    google.succeed();
    await tester.pump();
    api.response.complete(_session('google-user'));
    await tester.pumpAndSettle();
    expect(find.byType(HomeScreen), findsOneWidget);
    expect(
      tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
      0,
    );
  });

  for (final mode in ['login', 'register']) {
    for (final stale in [false, true]) {
      testWidgets(
        'pushed password $mode ${stale ? 'ignores a cleared attempt' : 'opens Home after committing session'}',
        (tester) async {
          final api = _ControlledApi();
          final container = await _pumpApp(
            tester,
            initialLocation: '/profile',
            api: api,
          );
          final entry = find.byKey(Key('profile-guest-$mode'));
          await tester.ensureVisible(entry);
          await tester.tap(entry);
          await tester.pumpAndSettle();
          if (mode == 'register') {
            await tester.enterText(
              find.byKey(const Key('auth-name')),
              'Tài khoản kiểm thử',
            );
          }
          await tester.enterText(
            find.byKey(const Key('auth-email')),
            'account@example.test',
          );
          await tester.enterText(
            find.byKey(const Key('auth-password')),
            'Fixture-password!2026',
          );
          final submit = find.byKey(const Key('auth-submit'));
          await tester.ensureVisible(submit);
          await tester.tap(submit);
          await tester.pump();
          if (stale) await container.read(sessionProvider.notifier).clear();
          api.response.complete(_session('google-user'));
          await tester.pumpAndSettle();
          if (stale) {
            expect(container.read(sessionProvider).value, isNull);
            expect(find.byType(AuthScreen), findsOneWidget);
            expect(find.byType(HomeScreen), findsNothing);
          } else {
            expect(
              container.read(sessionProvider).value?.userId,
              'google-user',
            );
            expect(find.byType(HomeScreen), findsOneWidget);
            expect(
              tester
                  .widget<NavigationBar>(find.byType(NavigationBar))
                  .selectedIndex,
              0,
            );
          }
          expect(tester.takeException(), isNull);
        },
      );
    }
  }

  testWidgets(
    'Google waits for backend verification and durable session then opens Home',
    (tester) async {
      final google = _ControlledGoogle();
      final api = _ControlledApi();
      final storage = _DelayedStorage();
      final container = await _pumpApp(
        tester,
        google: google,
        api: api,
        storage: storage,
      );
      await _tapGoogle(tester);
      google.succeed();
      await tester.pump();
      expect(container.read(sessionProvider).value, isNull);
      expect(find.byType(AuthScreen), findsOneWidget);
      api.response.complete(_session('google-user'));
      await tester.pump();
      expect(storage.sessionWriteStarted.isCompleted, isTrue);
      expect(container.read(sessionProvider).value, isNull);
      expect(find.byType(AuthScreen), findsOneWidget);
      storage.releaseWrite.complete();
      await tester.pumpAndSettle();
      expect(find.byType(HomeScreen), findsOneWidget);
      expect(find.byType(AuthScreen), findsNothing);
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        0,
      );
      expect(container.read(sessionProvider).value?.userId, 'google-user');
      expect(
        await storage.read(key: SessionController.storageKey),
        contains('test-refresh-google-user'),
      );
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'canceling Google keeps login available without creating a session',
    (tester) async {
      final google = _ControlledGoogle();
      final container = await _pumpApp(tester, google: google);
      await _tapGoogle(tester);
      google.result.completeError(
        const GoogleSignInException(code: GoogleSignInExceptionCode.canceled),
      );
      await tester.pumpAndSettle();
      expect(container.read(sessionProvider).value, isNull);
      expect(find.byType(AuthScreen), findsOneWidget);
      expect(find.byType(Notice), findsNothing);
      expect(
        tester
            .widget<OutlinedButton>(find.byKey(const Key('auth-google')))
            .onPressed,
        isNotNull,
      );
      expect(
        await const FlutterSecureStorage().read(
          key: SessionController.storageKey,
        ),
        isNull,
      );
    },
  );

  testWidgets('interrupted Google sign-in shows a retryable error', (
    tester,
  ) async {
    final google = _ControlledGoogle();
    final container = await _pumpApp(tester, google: google);
    await _tapGoogle(tester);
    google.result.completeError(
      const GoogleSignInException(code: GoogleSignInExceptionCode.interrupted),
    );
    await tester.pumpAndSettle();
    expect(container.read(sessionProvider).value, isNull);
    expect(find.byType(Notice), findsOneWidget);
    expect(find.byType(AuthScreen), findsOneWidget);
  });

  for (final failure in [
    const ApiFailure('invalid_external_token', status: 401),
    const ApiFailure('account_link_proof_required', status: 409),
    const ApiFailure('provider_email_required', status: 422),
    const ApiFailure('provider_not_configured', status: 503),
  ]) {
    testWidgets(
      'backend ${failure.status} displays an error and does not open Home',
      (tester) async {
        final google = _ControlledGoogle();
        final api = _ControlledApi();
        final container = await _pumpApp(tester, google: google, api: api);
        await _tapGoogle(tester);
        google.succeed();
        await tester.pump();
        api.response.completeError(failure);
        await tester.pumpAndSettle();
        expect(container.read(sessionProvider).value, isNull);
        expect(find.byType(Notice), findsOneWidget);
        expect(find.byType(AuthScreen), findsOneWidget);
        expect(find.byType(HomeScreen), findsNothing);
        expect(
          await const FlutterSecureStorage().read(
            key: SessionController.storageKey,
          ),
          isNull,
        );
      },
    );
  }

  for (final token in <String?>[null, '']) {
    testWidgets(
      'Google with ${token == null ? 'missing' : 'empty'} ID token cannot create an app session',
      (tester) async {
        final google = _ControlledGoogle();
        final container = await _pumpApp(tester, google: google);
        await _tapGoogle(tester);
        google.result.complete((idToken: token, displayName: null));
        await tester.pumpAndSettle();
        expect(container.read(sessionProvider).value, isNull);
        expect(find.byType(Notice), findsOneWidget);
        expect(find.byType(HomeScreen), findsNothing);
        expect(
          await const FlutterSecureStorage().read(
            key: SessionController.storageKey,
          ),
          isNull,
        );
      },
    );
  }

  testWidgets(
    'storage failure after Google verification stays on login with an error',
    (tester) async {
      final google = _ControlledGoogle();
      final api = _ControlledApi();
      final storage = _DelayedStorage(fail: true)..releaseWrite.complete();
      final container = await _pumpApp(
        tester,
        google: google,
        api: api,
        storage: storage,
      );
      await _tapGoogle(tester);
      google.succeed();
      await tester.pump();
      api.response.complete(_session('google-user'));
      await tester.pumpAndSettle();
      expect(container.read(sessionProvider).value, isNull);
      expect(find.byType(Notice), findsOneWidget);
      expect(find.byType(HomeScreen), findsNothing);
      expect(await storage.read(key: SessionController.storageKey), isNull);
    },
  );

  testWidgets(
    'a Google result arriving after session clear cannot navigate or persist',
    (tester) async {
      final google = _ControlledGoogle();
      final api = _ControlledApi();
      final container = await _pumpApp(tester, google: google, api: api);
      await _tapGoogle(tester);
      await container.read(sessionProvider.notifier).clear();
      google.succeed();
      await tester.pump();
      api.response.complete(_session('google-user'));
      await tester.pumpAndSettle();
      expect(container.read(sessionProvider).value, isNull);
      expect(find.byType(AuthScreen), findsOneWidget);
      expect(find.byType(HomeScreen), findsNothing);
      expect(
        await const FlutterSecureStorage().read(
          key: SessionController.storageKey,
        ),
        isNull,
      );
    },
  );

  for (final theme in ['dark', 'light']) {
    for (final mode in ['login', 'register', 'forgot', 'reset']) {
      testWidgets(
        '$mode remains usable at 320dp, 200% text with keyboard in $theme mode',
        (tester) async {
          tester.view.physicalSize = const Size(320, 640);
          tester.view.devicePixelRatio = 1;
          tester.view.viewInsets = const FakeViewPadding(bottom: 240);
          tester.platformDispatcher.textScaleFactorTestValue = 2;
          addTearDown(tester.view.resetPhysicalSize);
          addTearDown(tester.view.resetDevicePixelRatio);
          addTearDown(tester.view.resetViewInsets);
          addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
          await _pumpApp(
            tester,
            initialLocation: '/auth/$mode',
            settings: UserSettings(theme: theme),
          );
          final submit = find.byKey(const Key('auth-submit'));
          await tester.ensureVisible(submit);
          await tester.pumpAndSettle();
          expect(submit.hitTestable(), findsOneWidget);
          expect(tester.getSize(submit).height, greaterThanOrEqualTo(48));
          expect(
            tester.getSize(find.byKey(const Key('auth-content'))).width,
            lessThanOrEqualTo(272),
          );
          await tester.tap(submit);
          await tester.pumpAndSettle();
          expect(find.byType(AuthScreen), findsOneWidget);
          expect(tester.takeException(), isNull);
        },
      );
    }
  }
}
