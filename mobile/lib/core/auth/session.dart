import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../api/api_client.dart';

final sessionProvider = AsyncNotifierProvider<SessionController, Session?>(
  SessionController.new,
  retry: (_, _) => null,
);
final secureStorageProvider = Provider((ref) => const FlutterSecureStorage());
final authorizedApiProvider = Provider((ref) {
  final owner = ref.watch(sessionProvider.select((s) => s.value?.userId));
  final sessions = ref.read(sessionProvider.notifier);
  bool ownsSession() =>
      ref.mounted &&
      owner != null &&
      ref.read(sessionProvider).value?.userId == owner;
  final api = ApiClient(
    token: () async {
      if (!ownsSession()) return null;
      final token = await sessions.accessToken();
      // Refresh can span an account change. A retained client must never obtain
      // the next account's credentials, including on a transient request retry.
      return ownsSession() ? token : null;
    },
  );
  ref.onDispose(api.close);
  return api;
});

class Session {
  const Session({
    required this.accessToken,
    required this.refreshToken,
    required this.userId,
    required this.expiresAt,
  });

  Session.fromJson(Map<String, dynamic> j)
    : accessToken = j['accessToken'] as String,
      refreshToken = j['refreshToken'] as String,
      userId = j['userId'] as String,
      expiresAt = DateTime.parse(j['expiresAt'] as String);
  final String accessToken, refreshToken, userId;
  final DateTime expiresAt;
}

class SessionController extends AsyncNotifier<Session?> {
  static const storageKey = 'tmusic.v2.session';
  Future<Session?>? _rotating;
  Future<void> _storageWrites = Future.value();
  int _generation = 0;
  int _authenticationAttempt = 0;
  @override
  Future<Session?> build() async {
    final generation = _generation;
    final raw = await ref.read(secureStorageProvider).read(key: storageKey);
    if (generation != _generation) return state.value;
    if (raw == null) return null;
    String refreshToken, userId;
    try {
      final saved = jsonDecode(raw);
      if (saved is! Map ||
          saved['refreshToken'] is! String ||
          (saved['refreshToken'] as String).isEmpty ||
          saved['userId'] is! String ||
          (saved['userId'] as String).isEmpty) {
        throw const FormatException('Invalid stored session');
      }
      refreshToken = saved['refreshToken'] as String;
      userId = saved['userId'] as String;
    } on FormatException {
      await _serializeStorage(() async {
        if (generation == _generation) {
          await ref.read(secureStorageProvider).delete(key: storageKey);
        }
      });
      return generation == _generation ? null : state.value;
    }
    try {
      final restored = await _refresh(refreshToken, generation);
      return generation == _generation ? restored : state.value;
    } on ApiFailure catch (e) {
      if (generation != _generation) return state.value;
      if (e.status == null) {
        ref.read(offlineModeProvider.notifier).setOffline(true);
        return Session(
          accessToken: '',
          refreshToken: refreshToken,
          userId: userId,
          expiresAt: DateTime.fromMillisecondsSinceEpoch(0),
        );
      }
      rethrow;
    }
  }

  Future<bool> signIn(String email, String password) => _authenticate(
    '/auth/login',
    {'email': email, 'password': password, 'deviceName': 'TMusic mobile'},
  );

  Future<bool> signUp(String email, String password, String displayName) =>
      _authenticate('/auth/register', {
        'email': email,
        'password': password,
        'displayName': displayName,
        'deviceName': 'TMusic mobile',
      });

  Future<bool> _authenticate(String path, Map<String, dynamic> data) async {
    final acceptResult = beginAuthentication();
    final result = await ref
        .read(publicApiProvider)
        .request('POST', path, data: data);
    return acceptResult(result);
  }

  /// Fences password and native provider sign-in across all intervening awaits.
  /// Reports success only after this attempt's session is durably committed.
  Future<bool> Function(Map<String, dynamic>) beginAuthentication() {
    final generation = _generation;
    final attempt = ++_authenticationAttempt;
    return (result) async {
      if (generation != _generation || attempt != _authenticationAttempt) {
        return false;
      }
      return _accept(result, attempt: attempt);
    };
  }

  Future<void> accept(Map<String, dynamic> value) async {
    await _accept(value);
  }

  Future<bool> _accept(Map<String, dynamic> value, {int? attempt}) async {
    final s = Session.fromJson(value);
    final previous = state.value;
    final generation = ++_generation;
    _rotating = null;
    await _persist(s, generation);
    if (generation != _generation) return false;
    if (attempt != null && attempt != _authenticationAttempt) {
      // A new attempt can start while secure storage is writing. Restore the
      // last committed session so this discarded result cannot survive restart.
      if (previous != null) {
        await _persist(previous, generation);
      } else {
        await _serializeStorage(() async {
          if (generation == _generation) {
            await ref.read(secureStorageProvider).delete(key: storageKey);
          }
        });
      }
      return false;
    }
    state = AsyncData(s);
    return true;
  }

  Future<String?> accessToken() async {
    final s = state.value;
    if (s == null) return null;
    if (s.expiresAt.isAfter(DateTime.now().add(const Duration(seconds: 45)))) {
      return s.accessToken;
    }
    if (_rotating != null) return (await _rotating)?.accessToken;
    final generation = _generation;
    final rotating = _refresh(s.refreshToken, generation);
    _rotating = rotating;
    try {
      final updated = await rotating;
      if (generation != _generation) return null;
      state = AsyncData(updated);
      return updated?.accessToken;
    } finally {
      if (identical(_rotating, rotating)) _rotating = null;
    }
  }

  Future<Session?> _refresh(String token, int generation) async {
    try {
      final s = Session.fromJson(
        await ref
            .read(publicApiProvider)
            .request('POST', '/auth/refresh', data: {'refreshToken': token}),
      );
      if (generation != _generation) return null;
      await _persist(s, generation);
      return generation == _generation ? s : null;
    } on ApiFailure catch (e) {
      if (generation != _generation) return null;
      if (e.status != 401) rethrow;
      await clear();
      return null;
    }
  }

  Future<void> clear() async {
    final generation = ++_generation;
    _rotating = null;
    state = const AsyncData(null);
    await _serializeStorage(() async {
      if (generation == _generation) {
        await ref.read(secureStorageProvider).delete(key: storageKey);
      }
    });
  }

  Future<void> _persist(Session s, int generation) =>
      _serializeStorage(() async {
        if (generation != _generation) return;
        await ref
            .read(secureStorageProvider)
            .write(
              key: storageKey,
              value: jsonEncode({
                'refreshToken': s.refreshToken,
                'userId': s.userId,
              }),
            );
      });

  Future<void> _serializeStorage(Future<void> Function() action) {
    final write = _storageWrites.then((_) => action());
    _storageWrites = write.then((_) {}, onError: (Object _, StackTrace _) {});
    return write;
  }
}
