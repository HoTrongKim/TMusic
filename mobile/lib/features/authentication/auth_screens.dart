import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/external_auth.dart';
import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';

/// Splash / Session Restoration Screen
class SessionLoadingScreen extends ConsumerWidget {
  const SessionLoadingScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(sessionProvider);
    return Scaffold(
      body: SafeArea(
        child: session.hasError
            ? PageList(
                children: [
                  const SizedBox(height: 32),
                  Notice(context.tr('authRecovery'), error: true),
                  const SizedBox(height: 12),
                  OutlinedButton(
                    onPressed: () => ref.invalidate(sessionProvider),
                    child: Text(context.tr('retry')),
                  ),
                  const SizedBox(height: 8),
                  TextButton(
                    onPressed: () => ref.read(sessionProvider.notifier).clear(),
                    child: Text(context.tr('forgetSession')),
                  ),
                ],
              )
            : Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const TintedIconBadge(
                        icon: Icons.graphic_eq_rounded,
                        size: 72,
                        iconSize: 36,
                      ),
                      const SizedBox(height: 18),
                      const Text(
                        'TMUSIC',
                        style: TextStyle(
                          fontWeight: FontWeight.w800,
                          letterSpacing: 4,
                          fontSize: 18,
                        ),
                      ),
                      const SizedBox(height: 24),
                      CircularProgressIndicator(
                        semanticsLabel: context.tr('loading'),
                      ),
                    ],
                  ),
                ),
              ),
      ),
    );
  }
}

/// Matchy & Cross-inspired Value-First Music Onboarding Screen
class WelcomeScreen extends ConsumerStatefulWidget {
  const WelcomeScreen({super.key});

  @override
  ConsumerState<WelcomeScreen> createState() => _WelcomeScreenState();
}

class _WelcomeScreenState extends ConsumerState<WelcomeScreen> {
  bool _busy = false;
  Object? _error;

  Future<void> _signInExternal(String provider) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final signedIn = await ref
          .read(externalAuthProvider)
          .authenticate(provider);
      if (signedIn && mounted) context.go('/');
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final googleEnabled = ref.watch(googleIdentityProvider).isSupported;
    final capabilitiesAsync = (googleEnabled || ExternalAuth.appleEnabled)
        ? ref.watch(capabilitiesProvider)
        : null;
    final capabilities = capabilitiesAsync?.hasValue == true
        ? capabilitiesAsync!.value
        : null;

    return Scaffold(
      body: DecoratedBox(
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            stops: const [0.0, 0.45, 1.0],
            colors: [
              scheme.primaryContainer.withValues(alpha: 0.42),
              scheme.surfaceContainerLowest,
              scheme.surfaceContainerLowest,
            ],
          ),
        ),
        child: SafeArea(
          child: PageList(
            children: [
              const SizedBox(height: 28),
              Center(
                child: Column(
                  children: [
                    const TintedIconBadge(
                      icon: Icons.graphic_eq_rounded,
                      size: 68,
                      iconSize: 34,
                    ),
                    const SizedBox(height: 14),
                    Text(
                      'TMUSIC',
                      style: Theme.of(context).textTheme.labelLarge
                          ?.copyWith(letterSpacing: 3.5, color: scheme.primary),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 28),
              Text(
                context.tr('welcome'),
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.headlineLarge
                    ?.copyWith(fontSize: 28),
              ),
              const SizedBox(height: 12),
              Text(
                context.tr('welcomeBody'),
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyLarge
                    ?.copyWith(color: scheme.onSurfaceVariant),
              ),
              const SizedBox(height: 28),
              if (_busy) ...[
                const LinearProgressIndicator(),
                const SizedBox(height: 12),
              ],
              if (_error != null) ...[
                Notice(errorText(context, _error!), error: true),
                const SizedBox(height: 12),
              ],
              FilledButton(
                key: const Key('welcome-login'),
                onPressed: _busy ? null : () => context.go('/auth/login'),
                child: Text(context.tr('login')),
              ),
              const SizedBox(height: 10),
              OutlinedButton(
                key: const Key('welcome-register'),
                onPressed: _busy ? null : () => context.go('/auth/register'),
                child: Text(context.tr('register')),
              ),
              const SizedBox(height: 10),
              TextButton(
                key: const Key('welcome-guest'),
                onPressed: _busy ? null : () => context.go('/'),
                child: Text(context.tr('continueAsGuest')),
              ),
              if (googleEnabled && capabilities?['googleSignIn'] != false) ...[
                const SizedBox(height: 10),
                OutlinedButton.icon(
                  key: const Key('welcome-google'),
                  onPressed: _busy ? null : () => _signInExternal('google'),
                  icon: const Icon(Icons.account_circle_outlined),
                  label: Text(context.tr('google')),
                ),
              ],
              if (ExternalAuth.appleEnabled &&
                  capabilities?['appleSignIn'] != false) ...[
                const SizedBox(height: 10),
                OutlinedButton.icon(
                  key: const Key('welcome-apple'),
                  onPressed: _busy ? null : () => _signInExternal('apple'),
                  icon: const Icon(Icons.apple),
                  label: Text(context.tr('apple')),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Login, Instant Register, Forgot Password & Reset Password Studio Screens
class AuthScreen extends ConsumerStatefulWidget {
  const AuthScreen({super.key, required this.mode, this.email = ''});
  final String mode, email;

  @override
  ConsumerState<AuthScreen> createState() => _AuthScreenState();
}

class _AuthScreenState extends ConsumerState<AuthScreen> {
  final _form = GlobalKey<FormState>();
  late final _email = TextEditingController(text: widget.email);
  final _password = TextEditingController(),
      _code = TextEditingController(),
      _name = TextEditingController();
  bool _busy = false;
  bool _obscure = true;
  Object? _error;
  String? _notice;

  @override
  void didUpdateWidget(covariant AuthScreen old) {
    super.didUpdateWidget(old);
    if (widget.mode != old.mode) {
      _password.clear();
      _code.clear();
      _error = null;
      _notice = null;
      _obscure = true;
    }
  }

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _code.dispose();
    _name.dispose();
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action();
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _signInExternal(String provider) => _run(() async {
    _completeAuthentication(
      await ref.read(externalAuthProvider).authenticate(provider),
    );
  });

  void _completeAuthentication(bool accepted) {
    if (!accepted || !mounted) return;
    FocusScope.of(context).unfocus();
    // Auth can be pushed above a shell branch, whose underlying URI remains
    // /profile. A router refresh alone does not select Home in that case.
    context.go('/');
  }

  void _next(String mode) {
    context.go(
      Uri(
        path: '/auth/$mode',
        queryParameters: _email.text.trim().isEmpty
            ? null
            : {'email': _email.text.trim()},
      ).toString(),
    );
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    await _run(() async {
      final api = ref.read(publicApiProvider);
      final email = _email.text.trim();
      switch (widget.mode) {
        case 'login':
          _completeAuthentication(
            await ref
                .read(sessionProvider.notifier)
                .signIn(email, _password.text),
          );
        case 'register':
          _completeAuthentication(
            await ref
                .read(sessionProvider.notifier)
                .signUp(email, _password.text, _name.text.trim()),
          );
        case 'forgot':
          await api.request(
            'POST',
            '/auth/forgot-password',
            data: {'email': email},
          );
          if (mounted) _next('reset');
        case 'reset':
          await api.request(
            'POST',
            '/auth/reset-password',
            data: {
              'email': email,
              'code': _code.text.trim(),
              'password': _password.text,
            },
          );
          if (mounted) {
            _next('login');
            ScaffoldMessenger.of(context)
                .showSnackBar(SnackBar(content: Text(context.tr('resetDone'))));
          }
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final mode = widget.mode == 'verify' ? 'login' : widget.mode;
    final scheme = Theme.of(context).colorScheme;
    final googleEnabled = ref.watch(googleIdentityProvider).isSupported;
    final capabilitiesAsync = (googleEnabled || ExternalAuth.appleEnabled)
        ? ref.watch(capabilitiesProvider)
        : null;
    final capabilities = capabilitiesAsync?.hasValue == true
        ? capabilitiesAsync!.value
        : null;
    final showPassword = ['login', 'register', 'reset'].contains(mode);

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          tooltip: context.tr('backLogin'),
          onPressed: () =>
              context.go(mode == 'login' ? '/welcome' : '/auth/login'),
          icon: const Icon(Icons.arrow_back_rounded),
        ),
        title: Text(context.tr(mode)),
      ),
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, viewport) => SingleChildScrollView(
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            padding: const EdgeInsets.fromLTRB(24, 24, 24, 32),
            child: ConstrainedBox(
              constraints: BoxConstraints(
                minHeight: (viewport.maxHeight - 56).clamp(
                  0.0,
                  double.infinity,
                ),
              ),
              child: Center(
                child: ConstrainedBox(
                  key: const Key('auth-content'),
                  constraints: const BoxConstraints(maxWidth: 440),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (mode == 'login' || mode == 'register') ...[
                        const Center(
                          child: TintedIconBadge(
                            icon: Icons.graphic_eq_rounded,
                            size: 56,
                            iconSize: 28,
                          ),
                        ),
                        const SizedBox(height: 16),
                        Text(
                          'TMUSIC',
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.labelMedium
                              ?.copyWith(
                                color: scheme.primary,
                                letterSpacing: 3,
                              ),
                        ),
                        const SizedBox(height: 12),
                        Text(
                          context.tr(
                            mode == 'login'
                                ? 'authLoginTitle'
                                : 'authRegisterTitle',
                          ),
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.headlineLarge,
                        ),
                        const SizedBox(height: 8),
                        Text(
                          context.tr(
                            mode == 'login'
                                ? 'authLoginBody'
                                : 'authRegisterBody',
                          ),
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodyLarge
                              ?.copyWith(color: scheme.onSurfaceVariant),
                        ),
                        const SizedBox(height: 28),
                      ],
                      if (mode == 'reset') Notice(context.tr('resetSent')),
                      if (_busy) const LinearProgressIndicator(),
                      if (_error != null)
                        Notice(errorText(context, _error!), error: true),
                      if (_notice != null) Notice(_notice!),
                      const SizedBox(height: 8),
                      Form(
                        key: _form,
                        child: AutofillGroup(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              if (mode == 'register') ...[
                                TextFormField(
                                  key: const Key('auth-name'),
                                  controller: _name,
                                  decoration: InputDecoration(
                                    labelText: context.tr('name'),
                                    prefixIcon: const Icon(
                                      Icons.person_outline_rounded,
                                    ),
                                  ),
                                  maxLength: 80,
                                  validator: (v) => requiredText(context, v),
                                ),
                                const SizedBox(height: 14),
                              ],
                              TextFormField(
                                key: const Key('auth-email'),
                                controller: _email,
                                keyboardType: TextInputType.emailAddress,
                                autofillHints: const [AutofillHints.email],
                                autocorrect: false,
                                decoration: InputDecoration(
                                  labelText: context.tr('email'),
                                  prefixIcon: const Icon(
                                    Icons.alternate_email_rounded,
                                  ),
                                ),
                                validator: (v) =>
                                    RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$')
                                        .hasMatch((v ?? '').trim())
                                    ? null
                                    : context.tr('emailInvalid'),
                              ),
                              if (mode == 'reset') ...[
                                const SizedBox(height: 14),
                                TextFormField(
                                  key: const Key('auth-code'),
                                  controller: _code,
                                  autocorrect: false,
                                  decoration: InputDecoration(
                                    labelText: context.tr('code'),
                                  ),
                                  validator: (v) => requiredText(context, v),
                                ),
                              ],
                              if (showPassword) ...[
                                const SizedBox(height: 14),
                                TextFormField(
                                  key: const Key('auth-password'),
                                  controller: _password,
                                  obscureText: _obscure,
                                  autocorrect: false,
                                  autofillHints: [
                                    mode == 'login'
                                        ? AutofillHints.password
                                        : AutofillHints.newPassword,
                                  ],
                                  decoration: InputDecoration(
                                    labelText: context.tr('password'),
                                    prefixIcon: const Icon(
                                      Icons.lock_outline_rounded,
                                    ),
                                    helperText: mode == 'login'
                                        ? null
                                        : context.tr('passwordHint'),
                                    helperMaxLines: 5,
                                    suffixIcon: IconButton(
                                      tooltip: context.tr(
                                        _obscure
                                            ? 'showPassword'
                                            : 'hidePassword',
                                      ),
                                      onPressed: () =>
                                          setState(() => _obscure = !_obscure),
                                      icon: Icon(
                                        _obscure
                                            ? Icons.visibility_outlined
                                            : Icons.visibility_off_outlined,
                                      ),
                                    ),
                                  ),
                                  validator: (v) => requiredText(context, v),
                                ),
                              ],
                              const SizedBox(height: 22),
                              FilledButton(
                                key: const Key('auth-submit'),
                                onPressed: _busy ? null : _submit,
                                child: Text(context.tr(mode)),
                              ),
                            ],
                          ),
                        ),
                      ),
                      if (mode == 'login') ...[
                        const SizedBox(height: 10),
                        Align(
                          alignment: Alignment.centerRight,
                          child: TextButton(
                            onPressed: _busy ? null : () => _next('forgot'),
                            child: Text(context.tr('forgot')),
                          ),
                        ),
                      ],
                      if (mode == 'login' || mode == 'register') ...[
                        if ((googleEnabled &&
                                capabilities?['googleSignIn'] != false) ||
                            (ExternalAuth.appleEnabled &&
                                capabilities?['appleSignIn'] != false)) ...[
                          const SizedBox(height: 16),
                          Row(
                            children: [
                              const Expanded(child: Divider()),
                              Flexible(
                                flex: 3,
                                child: Padding(
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 12,
                                  ),
                                  child: Text(
                                    context.tr('authOrContinue'),
                                    textAlign: TextAlign.center,
                                    style: Theme.of(context).textTheme.bodySmall
                                        ?.copyWith(
                                          color: scheme.onSurfaceVariant,
                                        ),
                                  ),
                                ),
                              ),
                              const Expanded(child: Divider()),
                            ],
                          ),
                          const SizedBox(height: 8),
                        ],
                        if (googleEnabled &&
                            capabilities?['googleSignIn'] != false) ...[
                          const SizedBox(height: 10),
                          OutlinedButton.icon(
                            key: const Key('auth-google'),
                            onPressed: _busy
                                ? null
                                : () => _signInExternal('google'),
                            icon: const Icon(Icons.account_circle_outlined),
                            label: Text(context.tr('google')),
                          ),
                        ],
                        if (ExternalAuth.appleEnabled &&
                            capabilities?['appleSignIn'] != false) ...[
                          const SizedBox(height: 10),
                          OutlinedButton.icon(
                            key: const Key('auth-apple'),
                            onPressed: _busy
                                ? null
                                : () => _signInExternal('apple'),
                            icon: const Icon(Icons.apple),
                            label: Text(context.tr('apple')),
                          ),
                        ],
                        const SizedBox(height: 20),
                        Wrap(
                          alignment: WrapAlignment.center,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            Text(
                              context.tr(
                                mode == 'login'
                                    ? 'authNoAccount'
                                    : 'authHasAccount',
                              ),
                              textAlign: TextAlign.center,
                              style: Theme.of(context).textTheme.bodyMedium
                                  ?.copyWith(color: scheme.onSurfaceVariant),
                            ),
                            TextButton(
                              key: const Key('auth-switch-mode'),
                              onPressed: _busy
                                  ? null
                                  : () => _next(
                                      mode == 'login' ? 'register' : 'login',
                                    ),
                              child: Text(
                                context.tr(
                                  mode == 'login' ? 'register' : 'login',
                                ),
                              ),
                            ),
                          ],
                        ),
                      ],
                      if (mode == 'reset') ...[
                        const SizedBox(height: 8),
                        TextButton(
                          onPressed: _busy
                              ? null
                              : () => _run(() async {
                                  if (!_email.text.contains('@')) return;
                                  await ref
                                      .read(publicApiProvider)
                                      .request(
                                        'POST',
                                        '/auth/forgot-password',
                                        data: {'email': _email.text.trim()},
                                      );
                                  if (mounted) {
                                    setState(
                                      () => _notice = context.tr('resetSent'),
                                    );
                                  }
                                }),
                          child: Text(context.tr('resend')),
                        ),
                      ],
                      if (mode != 'login' && mode != 'register') ...[
                        const SizedBox(height: 8),
                        TextButton(
                          onPressed: _busy ? null : () => _next('login'),
                          child: Text(context.tr('backLogin')),
                        ),
                      ],
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
