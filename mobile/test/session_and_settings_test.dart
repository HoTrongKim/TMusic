import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';
import 'package:tmusic/features/settings/settings_controller.dart';

Map<String, dynamic> _session(String user, {bool expired = false}) => {
  'userId': user,
  'accessToken': 'access-$user',
  'refreshToken': 'refresh-$user',
  'expiresAt': DateTime.now()
      .add(Duration(minutes: expired ? -1 : 10))
      .toIso8601String(),
};

class _ControlledApi extends ApiClient {
  final refresh = Completer<Map<String, dynamic>>();
  final refreshStarted = Completer<void>();
  Completer<Map<String, dynamic>>? login;
  Completer<Map<String, dynamic>>? settingsRead;
  final settingsReadStarted = Completer<void>();
  int refreshCalls = 0;
  bool offlineSettings = false;
  Map<String, dynamic> settings = {'theme': 'system', 'language': 'en'};

  @override
  Future<Map<String, dynamic>> request(
    String method,
    String path, {
    Map<String, dynamic>? data,
    Map<String, dynamic>? query,
    String? version,
    Map<String, dynamic>? headers,
  }) async {
    if (path == '/auth/login' && login != null) return login!.future;
    if (path == '/auth/refresh') {
      refreshCalls++;
      if (!refreshStarted.isCompleted) refreshStarted.complete();
      return refresh.future;
    }
    if (path == '/users/me/settings') {
      if (offlineSettings) throw const ApiFailure('network_error');
      if (method == 'GET' && settingsRead != null) {
        if (!settingsReadStarted.isCompleted) settingsReadStarted.complete();
        return settingsRead!.future;
      }
      if (method == 'PATCH') settings = {...?data};
      return settings;
    }
    throw StateError('Unexpected $method $path');
  }
}

ProviderContainer _container(_ControlledApi api) => ProviderContainer(
  overrides: [
    publicApiProvider.overrideWithValue(api),
    authorizedApiProvider.overrideWithValue(api),
  ],
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  test('refresh completing after logout cannot restore a session', () async {
    final api = _ControlledApi();
    final c = _container(api);
    addTearDown(c.dispose);
    await c.read(sessionProvider.future);
    final controller = c.read(sessionProvider.notifier);
    await controller.accept(_session('old', expired: true));
    final pending = controller.accessToken();
    await api.refreshStarted.future;
    await controller.clear();
    api.refresh.complete(_session('old'));
    expect(await pending, isNull);
    expect(c.read(sessionProvider).value, isNull);
    expect(
      await const FlutterSecureStorage().read(
        key: SessionController.storageKey,
      ),
      isNull,
    );
  });

  test('late refresh cannot overwrite another signed-in account', () async {
    final api = _ControlledApi();
    final c = _container(api);
    addTearDown(c.dispose);
    await c.read(sessionProvider.future);
    final controller = c.read(sessionProvider.notifier);
    await controller.accept(_session('old', expired: true));
    final pending = controller.accessToken();
    await api.refreshStarted.future;
    await controller.accept(_session('new'));
    api.refresh.complete(_session('old'));
    expect(await pending, isNull);
    expect(c.read(sessionProvider).value?.userId, 'new');
    final stored = jsonDecode(
      (await const FlutterSecureStorage().read(
        key: SessionController.storageKey,
      ))!,
    ) as Map;
    expect(stored['userId'], 'new');
  });

  test('late unauthorized refresh cannot clear a newer account', () async {
    final api = _ControlledApi();
    final c = _container(api);
    addTearDown(c.dispose);
    await c.read(sessionProvider.future);
    final controller = c.read(sessionProvider.notifier);
    await controller.accept(_session('old', expired: true));
    final pending = controller.accessToken();
    await api.refreshStarted.future;
    await controller.accept(_session('new'));
    api.refresh.completeError(const ApiFailure('session_expired', status: 401));
    expect(await pending, isNull);
    expect(c.read(sessionProvider).value?.userId, 'new');
  });

  test('concurrent requests rotate an expired refresh token once', () async {
    final api = _ControlledApi();
    final c = _container(api);
    addTearDown(c.dispose);
    await c.read(sessionProvider.future);
    final controller = c.read(sessionProvider.notifier);
    await controller.accept(_session('same', expired: true));
    final pending = Future.wait([
      controller.accessToken(),
      controller.accessToken(),
    ]);
    await api.refreshStarted.future;
    api.refresh.complete(_session('same'));
    expect(await pending, ['access-same', 'access-same']);
    expect(api.refreshCalls, 1);
  });

  test(
    'late login response after clearing session cannot sign back in',
    () async {
      final api = _ControlledApi()..login = Completer<Map<String, dynamic>>();
      final c = _container(api);
      addTearDown(c.dispose);
      await c.read(sessionProvider.future);
      final controller = c.read(sessionProvider.notifier);
      final pending = controller.signIn('fixture@example.test', 'fixture-only');
      await controller.clear();
      api.login!.complete(_session('stale'));
      await pending;
      expect(c.read(sessionProvider).value, isNull);
    },
  );

  test(
    'late settings read cannot overwrite a newer saved preference',
    () async {
      final api = _ControlledApi();
      final c = _container(api);
      addTearDown(c.dispose);
      await c.read(sessionProvider.future);
      await c.read(sessionProvider.notifier).accept(_session('account'));
      await c.read(settingsProvider.future);
      api.settingsRead = Completer<Map<String, dynamic>>();
      c.invalidate(settingsProvider);
      final loading = c.read(settingsProvider.future);
      await api.settingsReadStarted.future;
      await c
          .read(settingsProvider.notifier)
          .save(theme: 'light', language: 'vi');
      api.settingsRead!.complete({'theme': 'dark', 'language': 'en'});
      await loading;
      await Future<void>.delayed(Duration.zero);
      expect(c.read(settingsProvider).value?.theme, 'light');
      expect(c.read(settingsProvider).value?.language, 'vi');
      api.offlineSettings = true;
      c.invalidate(settingsProvider);
      expect((await c.read(settingsProvider.future)).theme, 'light');
    },
  );

  test(
    'native auth result cannot replace a newer authentication attempt',
    () async {
      final c = _container(_ControlledApi());
      addTearDown(c.dispose);
      await c.read(sessionProvider.future);
      final controller = c.read(sessionProvider.notifier);
      final oldResult = controller.beginAuthentication();
      final newResult = controller.beginAuthentication();
      await newResult(_session('new'));
      await oldResult(_session('old'));
      expect(c.read(sessionProvider).value?.userId, 'new');
      final canceled = controller.beginAuthentication();
      await controller.clear();
      await canceled(_session('canceled'));
      expect(c.read(sessionProvider).value, isNull);
    },
  );

  test('corrupt persisted session recovers to guest mode', () async {
    FlutterSecureStorage.setMockInitialValues({
      SessionController.storageKey: '{invalid',
    });
    final c = _container(_ControlledApi());
    addTearDown(c.dispose);
    expect(await c.read(sessionProvider.future), isNull);
    expect(
      await const FlutterSecureStorage().read(
        key: SessionController.storageKey,
      ),
      isNull,
    );
  });

  test('guest theme and language survive controller recreation', () async {
    final api = _ControlledApi();
    final first = _container(api);
    await first.read(sessionProvider.future);
    await first.read(settingsProvider.future);
    await first
        .read(settingsProvider.notifier)
        .save(theme: 'light', language: 'en');
    first.dispose();
    final second = _container(api);
    addTearDown(second.dispose);
    await second.read(sessionProvider.future);
    final restored = await second.read(settingsProvider.future);
    expect(restored.theme, 'light');
    expect(restored.language, 'en');
  });

  test('account settings preserve the explicit system theme', () async {
    final c = _container(_ControlledApi());
    addTearDown(c.dispose);
    await c.read(sessionProvider.future);
    await c.read(sessionProvider.notifier).accept(_session('account'));
    final settings = await c.read(settingsProvider.future);
    expect(settings.theme, 'system');
    expect(settings.mode, ThemeMode.system);
  });

  test(
    'saved account settings are available offline and scoped per user',
    () async {
      final api = _ControlledApi();
      final c = _container(api);
      addTearDown(c.dispose);
      await c.read(sessionProvider.future);
      await c.read(sessionProvider.notifier).accept(_session('account-a'));
      await c.read(settingsProvider.future);
      await c
          .read(settingsProvider.notifier)
          .save(theme: 'light', language: 'en');
      api.offlineSettings = true;
      c.invalidate(settingsProvider);
      expect((await c.read(settingsProvider.future)).theme, 'light');
      await c.read(sessionProvider.notifier).accept(_session('account-b'));
      final other = await c.read(settingsProvider.future);
      expect(other.theme, 'dark');
      expect(other.language, 'vi');
    },
  );
}
