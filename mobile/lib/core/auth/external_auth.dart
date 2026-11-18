import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_sign_in/google_sign_in.dart';
import 'package:sign_in_with_apple/sign_in_with_apple.dart';

import '../api/api_client.dart';
import 'session.dart';

final externalAuthProvider = Provider((ref) => ExternalAuth(ref));
final googleIdentityProvider = Provider((ref) => GoogleIdentityClient());
final capabilitiesProvider = FutureProvider.autoDispose(
  (ref) => ref.read(publicApiProvider).request('GET', '/system/capabilities'),
  retry: (_, _) => null,
);

class ExternalAuth {
  ExternalAuth(this.ref);
  final Ref ref;

  static const _rawServerId = String.fromEnvironment(
    'TMUSIC_GOOGLE_SERVER_CLIENT_ID',
    defaultValue: String.fromEnvironment('TMUSIC_GOOGLE_WEB_CLIENT_ID'),
  );
  static const _rawAndroidId = String.fromEnvironment(
    'TMUSIC_GOOGLE_ANDROID_CLIENT_ID',
  );
  static const _rawIosId = String.fromEnvironment(
    'TMUSIC_GOOGLE_IOS_CLIENT_ID',
  );

  static String _cleanGoogleClientId(String raw) {
    var s = raw.trim();
    while (s.endsWith('\\') || s.endsWith('/')) {
      s = s.substring(0, s.length - 1).trim();
    }
    while (s.toLowerCase().endsWith('.apps.googleusercontent.comcom')) {
      s = s.substring(0, s.length - 3);
    }
    return s;
  }

  static final String serverId = _cleanGoogleClientId(_rawServerId);
  static final String androidId = _cleanGoogleClientId(_rawAndroidId);
  static final String iosId = _cleanGoogleClientId(_rawIosId);

  static bool get googleEnabled =>
      (Platform.isAndroid && serverId.isNotEmpty) ||
      (Platform.isIOS && iosId.isNotEmpty);

  static bool get appleEnabled =>
      Platform.isIOS &&
      const bool.fromEnvironment('TMUSIC_ENABLE_APPLE_SIGN_IN');

  Future<bool> authenticate(String provider, {String? password}) async {
    final acceptSession = password == null
        ? ref.read(sessionProvider.notifier).beginAuthentication()
        : null;
    if (ref.read(capabilitiesProvider).hasError) {
      ref.invalidate(capabilitiesProvider);
    }
    final capabilities = await ref.read(capabilitiesProvider.future);
    if (capabilities[provider == 'google' ? 'googleSignIn' : 'appleSignIn'] !=
        true) {
      throw const ApiFailure('provider_not_configured', status: 503);
    }
    final linking = password != null;
    final api = ref.read(linking ? authorizedApiProvider : publicApiProvider);
    String? token;
    String? authorizationCode;
    String? displayName;
    try {
      if (provider == 'google' &&
          ref.read(googleIdentityProvider).isSupported) {
        final identity = await ref.read(googleIdentityProvider).authenticate();
        token = identity.idToken;
        displayName = identity.displayName;
      } else if (provider == 'apple' && appleEnabled) {
        final challenge = await api.request(
          'POST',
          linking ? '/auth/links/apple/challenge' : '/auth/apple/challenge',
        );
        final credential = await SignInWithApple.getAppleIDCredential(
          scopes: [
            AppleIDAuthorizationScopes.email,
            AppleIDAuthorizationScopes.fullName,
          ],
          nonce: challenge['nonce'] as String,
        );
        token = credential.identityToken;
        authorizationCode = credential.authorizationCode;
        final parts = [
          credential.givenName,
          credential.familyName,
        ].whereType<String>().where((s) => s.trim().isNotEmpty).toList();
        if (parts.isNotEmpty) displayName = parts.join(' ');
      } else {
        throw const ApiFailure('provider_not_configured', status: 503);
      }
    } on GoogleSignInException catch (e) {
      if (e.code == GoogleSignInExceptionCode.canceled) {
        return false;
      }
      if (e.code == GoogleSignInExceptionCode.interrupted) {
        throw const ApiFailure('google_auth_interrupted', status: 503);
      }
      throw const ApiFailure('google_oauth_config', status: 503);
    } on SignInWithAppleAuthorizationException catch (e) {
      if (e.code == AuthorizationErrorCode.canceled) return false;
      rethrow;
    }
    if (token == null || token.trim().isEmpty) {
      throw const ApiFailure('invalid_external_token', status: 401);
    }
    final result = await api.request(
      'POST',
      linking ? '/auth/links/$provider' : '/auth/$provider',
      data: {
        'idToken': token,
        'authorizationCode': ?authorizationCode,
        'displayName': ?displayName,
        'password': ?password,
        'deviceName': 'TMusic mobile',
      },
    );
    return linking ? true : await acceptSession!(result);
  }
}

/// The native SDK boundary. App sessions always come from verified API results.
class GoogleIdentityClient {
  Future<void>? _initialized;
  bool get isSupported => ExternalAuth.googleEnabled;

  Future<({String? idToken, String? displayName})> authenticate() async {
    final google = GoogleSignIn.instance;
    try {
      await (_initialized ??= google.initialize(
        clientId: Platform.isIOS ? ExternalAuth.iosId : null,
        serverClientId: ExternalAuth.serverId.isNotEmpty
            ? ExternalAuth.serverId
            : null,
      ));
    } catch (_) {
      _initialized = null;
      rethrow;
    }
    final account = await google.authenticate();
    return (
      idToken: account.authentication.idToken,
      displayName: account.displayName,
    );
  }
}
