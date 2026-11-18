import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tmusic/core/api/api_client.dart';
import 'package:tmusic/core/auth/session.dart';

Map<String, dynamic> _session(
  String owner, {
  bool expired = false,
  String? accessToken,
}) => {
  'userId': owner,
  'accessToken': accessToken ?? 'access-$owner',
  'refreshToken': 'refresh-$owner',
  'expiresAt': DateTime.now()
      .add(Duration(minutes: expired ? -1 : 10))
      .toIso8601String(),
};

ResponseBody _json(Map<String, dynamic> value, {int status = 200}) =>
    ResponseBody.fromString(
      jsonEncode(value),
      status,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );

class _Transport implements HttpClientAdapter {
  _Transport(this.respond);
  final Future<ResponseBody> Function(RequestOptions) respond;
  final authorization = <Object?>[];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) {
    authorization.add(options.headers['Authorization']);
    return respond(options);
  }

  @override
  void close({bool force = false}) {}
}

ApiClient _client(_Transport transport, {Future<String?> Function()? token}) {
  final dio = Dio(BaseOptions(baseUrl: 'https://tmusic.test/api/v1'))
    ..httpClientAdapter = transport;
  final client = ApiClient(token: token, dio: dio);
  addTearDown(client.close);
  return client;
}

// Keep the provider's real owner-bound token callback and the real ApiClient's
// retry/header behavior; replace only the external HTTP transport.
ApiClient _authorizedClient(ApiClient providerClient, _Transport transport) =>
    _client(transport, token: providerClient.token);

Future<Object?> _outcome(Future<Map<String, dynamic>> request) => request.then(
  (value) => value,
  onError: (Object error, StackTrace _) => error,
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  test(
    'an account A request cannot retry with account B credentials',
    () async {
      final container = ProviderContainer();
      addTearDown(container.dispose);
      await container.read(sessionProvider.future);
      final sessions = container.read(sessionProvider.notifier);
      await sessions.accept(_session('a'));
      final accountA = container.read(authorizedApiProvider);
      final started = Completer<void>();
      final firstResponse = Completer<ResponseBody>();
      var calls = 0;
      final transport = _Transport((_) async {
        if (++calls == 1) {
          started.complete();
          return firstResponse.future;
        }
        return _json({'ok': true});
      });
      final oldClient = _authorizedClient(accountA, transport);
      final pending = _outcome(oldClient.request('GET', '/protected'));
      await started.future;

      await sessions.accept(_session('b'));
      final accountB = container.read(authorizedApiProvider);
      firstResponse.complete(_json({'code': 'TEMPORARY'}, status: 503));
      final result = await pending;

      expect(transport.authorization, ['Bearer access-a']);
      expect(
        result,
        isA<ApiFailure>()
            .having((e) => e.code, 'code', 'session_expired')
            .having((e) => e.status, 'status', 401),
      );
      final currentTransport = _Transport((_) async => _json({'ok': true}));
      await _authorizedClient(
        accountB,
        currentTransport,
      ).request('GET', '/protected');
      expect(currentTransport.authorization, ['Bearer access-b']);
      expect(identical(accountA, accountB), isFalse);
    },
  );

  test(
    'an owner change during token refresh cannot authorize the old request',
    () async {
      final refreshStarted = Completer<void>();
      final refreshResponse = Completer<ResponseBody>();
      final publicTransport = _Transport((options) async {
        expect(options.path, '/auth/refresh');
        expect(options.data, {'refreshToken': 'refresh-a'});
        refreshStarted.complete();
        return refreshResponse.future;
      });
      final container = ProviderContainer(
        overrides: [
          publicApiProvider.overrideWithValue(_client(publicTransport)),
        ],
      );
      addTearDown(container.dispose);
      await container.read(sessionProvider.future);
      final sessions = container.read(sessionProvider.notifier);
      await sessions.accept(_session('a', expired: true));
      final accountA = container.read(authorizedApiProvider);
      final transport = _Transport((_) async => _json({'ok': true}));
      final pending = _outcome(
        _authorizedClient(accountA, transport).request('GET', '/protected'),
      );
      await refreshStarted.future;

      await sessions.accept(_session('b'));
      container.read(authorizedApiProvider);
      refreshResponse.complete(_json(_session('a', accessToken: 'rotated-a')));

      expect(await pending, isA<ApiFailure>());
      expect(transport.authorization, isEmpty);
      expect(container.read(sessionProvider).value?.userId, 'b');
      expect(await accountA.token!(), isNull);
    },
  );

  test(
    'same-owner token rotation still authorizes with the refreshed token',
    () async {
      final publicTransport = _Transport((options) async {
        expect(options.path, '/auth/refresh');
        expect(options.data, {'refreshToken': 'refresh-a'});
        return _json(_session('a', accessToken: 'rotated-a'));
      });
      final container = ProviderContainer(
        overrides: [
          publicApiProvider.overrideWithValue(_client(publicTransport)),
        ],
      );
      addTearDown(container.dispose);
      await container.read(sessionProvider.future);
      await container
          .read(sessionProvider.notifier)
          .accept(_session('a', expired: true));
      final before = container.read(authorizedApiProvider);
      final transport = _Transport((_) async => _json({'ok': true}));

      await _authorizedClient(before, transport).request('GET', '/protected');

      expect(transport.authorization, ['Bearer rotated-a']);
      expect(identical(before, container.read(authorizedApiProvider)), isTrue);
    },
  );

  test(
    'a retained token callback returns no credentials after disposal',
    () async {
      final container = ProviderContainer();
      await container.read(sessionProvider.future);
      await container.read(sessionProvider.notifier).accept(_session('a'));
      final token = container.read(authorizedApiProvider).token!;
      container.dispose();

      expect(await token(), isNull);
    },
  );
}
