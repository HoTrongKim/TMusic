import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../shared/models/library_models.dart';
import '../api/api_client.dart';
import '../auth/session.dart';
import '../localization/app_copy.dart';

String errorText(BuildContext context, Object error) {
  if (error is ApiFailure) {
    if (error.status == 429) return context.tr('tooManyRequests');
    final failureCopy = switch (error.code.toUpperCase()) {
      'ACCOUNT_LINK_PROOF_REQUIRED' => 'externalAccountProof',
      'INVALID_EXTERNAL_TOKEN' => 'externalTokenInvalid',
      'PROVIDER_EMAIL_REQUIRED' => 'providerEmailRequired',
      'SOCIAL_AUTH_UNAVAILABLE' ||
      'PROVIDER_NOT_CONFIGURED' => 'socialAuthUnavailable',
      'INVALID_SOURCE_URL' ||
      'INVALID_TIKTOK_URL' ||
      'INVALID_SOUNDCLOUD_URL' ||
      'UNSUPPORTED_SOURCE_URL' => 'sourceUrlInvalid',
      'UNSAFE_SOURCE_REDIRECT' => 'sourceRedirectUnsupported',
      'TIKTOK_SHORT_UNRESOLVED' ||
      'SOUNDCLOUD_SHORT_UNRESOLVED' ||
      'TOO_MANY_SOURCE_REDIRECTS' => 'sourceShortLinkUnresolved',
      'SOURCE_UNAVAILABLE' => 'sourceContentUnavailable',
      'TIKTOK_IMPORT_DISABLED' => 'tiktokImportDisabled',
      'SOUNDCLOUD_IMPORT_DISABLED' => 'soundcloudImportDisabled',
      'SOURCE_ACCESS_CHECK_REQUIRED' => 'sourceAccessCheckRequired',
      'SOURCE_AUTHENTICATION_REQUIRED' => 'sourceAuthenticationRequired',
      'SOURCE_ACCESS_FORBIDDEN' => 'sourceAccessForbidden',
      'SOURCE_NETWORK_ERROR' => 'sourceNetworkError',
      'SOURCE_RESTRICTED_OR_UNSUPPORTED' => 'sourceRestricted',
      'SOURCE_RESPONSE_UNSUPPORTED' => 'sourceResponseUnsupported',
      'SOURCE_AUDIO_UNAVAILABLE' => 'sourceAudioUnavailable',
      'PROVIDER_TEMPORARILY_UNAVAILABLE' => 'sourceTemporarilyUnavailable',
      'RATE_LIMITED' => 'tooManyRequests',
      'NETWORK_ERROR' => 'connection',
      _ => null,
    };
    if (failureCopy != null) return context.tr(failureCopy);
    if (error.code == 'google_auth_interrupted') {
      return context.tr('googleInterrupted');
    }
    if (error.code == 'google_oauth_config') {
      return error.detail ?? context.tr('googleConfigError');
    }
    if (error.status == 409 || error.status == 412) {
      return error.detail ?? context.tr('conflict');
    }
    if (error.status == 401) {
      return error.detail ?? context.tr('sessionExpired');
    }
    return error.detail ??
        context.tr(error.status == null ? 'connection' : 'genericError');
  }
  return context.tr('genericError');
}

String downloadFailureText(BuildContext context, String? errorCode) {
  final key = switch (errorCode?.trim().toUpperCase()) {
    'SOURCE_ACCESS_CHECK_REQUIRED' => 'sourceAccessCheckRequired',
    'SOURCE_AUTHENTICATION_REQUIRED' => 'sourceAuthenticationRequired',
    'SOURCE_ACCESS_FORBIDDEN' => 'sourceAccessForbidden',
    'SOURCE_NETWORK_ERROR' || 'NETWORK_ERROR' => 'sourceNetworkError',
    'SOURCE_RESTRICTED_OR_UNSUPPORTED' => 'sourceRestricted',
    'AUDIO_STORAGE_UNAVAILABLE' => 'sourceTemporarilyUnavailable',
    'SOURCE_AUDIO_UNAVAILABLE' => 'sourceAudioUnavailable',
    'SOURCE_RESPONSE_UNSUPPORTED' => 'sourceResponseUnsupported',
    'SOURCE_UNAVAILABLE' => 'sourceContentUnavailable',
    'AUDIO_CONVERSION_FAILED' => 'audioConversionFailed',
    'RATE_LIMITED' => 'tooManyRequests',
    'AUDIO_DOWNLOAD_UNAVAILABLE' ||
    'AUDIO_ENGINE_UNAVAILABLE' ||
    'AUDIO_DOWNLOAD_BUSY' ||
    'PROVIDER_TEMPORARILY_UNAVAILABLE' => 'sourceTemporarilyUnavailable',
    _ => 'mp3Failed',
  };
  return context.tr(key);
}

String? formatDuration(int? seconds) {
  if (seconds == null || seconds <= 0) return null;
  final h = seconds ~/ 3600;
  final m = (seconds % 3600) ~/ 60;
  final s = seconds % 60;
  final mm = m.toString().padLeft(h > 0 ? 2 : 1, '0');
  final ss = s.toString().padLeft(2, '0');
  return h > 0 ? '$h:$mm:$ss' : '$mm:$ss';
}

/// Computes Spotify-style dynamic ambient header color from album cover preset or item seed.
Color albumAmbientColor(String? coverOrSeed, {bool isDark = true}) {
  return switch (coverOrSeed) {
    'cover:terracotta' || 'avatar:terracotta' => const Color(0xFF7D2E1E),
    'cover:green' || 'avatar:green' => const Color(0xFF165B33),
    'cover:navy' || 'avatar:navy' => const Color(0xFF1F3864),
    'cover:blue' || 'avatar:blue' => const Color(0xFF1E4D7B),
    'cover:amber' || 'avatar:amber' => const Color(0xFF784E14),
    'cover:slate' || 'avatar:slate' => const Color(0xFF37434F),
    _ => () {
      const palette = <Color>[
        Color(0xFF6B281E), // Warm terracotta (Screenshot #2)
        Color(0xFF1A4F37), // Deep Spotify forest
        Color(0xFF233B63), // Midnight indigo
        Color(0xFF4D2958), // Studio plum
        Color(0xFF634619), // Warm vinyl amber
        Color(0xFF1B4D54), // Deep teal
      ];
      final raw = coverOrSeed ?? 'tmusic';
      var hash = 0;
      for (final c in raw.codeUnits) {
        hash = (hash * 31 + c) & 0x7fffffff;
      }
      final base = palette[hash % palette.length];
      return isDark ? base : base.withValues(alpha: 0.24);
    }(),
  };
}

class TintedIconBadge extends StatelessWidget {
  const TintedIconBadge({
    super.key,
    required this.icon,
    this.color,
    this.size = 40,
    this.iconSize = 20,
  });

  final IconData icon;
  final Color? color;
  final double size;
  final double iconSize;

  @override
  Widget build(BuildContext context) {
    final c = color ?? Theme.of(context).colorScheme.primary;
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: c.withValues(alpha: 0.15),
        shape: BoxShape.circle,
      ),
      alignment: Alignment.center,
      child: Icon(icon, size: iconSize, color: c),
    );
  }
}

class SegmentedPillSelector<T> extends StatelessWidget {
  const SegmentedPillSelector({
    super.key,
    required this.options,
    required this.selected,
    required this.onChanged,
  });

  final List<({T value, String label, IconData? icon})> options;
  final T selected;
  final ValueChanged<T>? onChanged;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        children: [
          for (final opt in options)
            Expanded(
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 2),
                child: Material(
                  color: opt.value == selected
                      ? scheme.primary
                      : Colors.transparent,
                  shape: const StadiumBorder(),
                  child: InkWell(
                    customBorder: const StadiumBorder(),
                    onTap: onChanged == null
                        ? null
                        : () => onChanged!(opt.value),
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(minHeight: 38),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 8,
                          vertical: 7,
                        ),
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            if (opt.icon != null) ...[
                              Icon(
                                opt.icon,
                                size: 15,
                                color: opt.value == selected
                                    ? scheme.onPrimary
                                    : scheme.onSurfaceVariant,
                              ),
                              const SizedBox(width: 5),
                            ],
                            Flexible(
                              child: Text(
                                opt.label,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                textAlign: TextAlign.center,
                                style: Theme.of(context).textTheme.labelLarge
                                    ?.copyWith(
                                      fontSize: 12.5,
                                      fontWeight: opt.value == selected
                                          ? FontWeight.w800
                                          : FontWeight.w600,
                                      color: opt.value == selected
                                          ? scheme.onPrimary
                                          : scheme.onSurfaceVariant,
                                    ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class OfflineBanner extends ConsumerWidget {
  const OfflineBanner({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final offline = ref.watch(offlineModeProvider);
    if (!offline) return const SizedBox.shrink();
    final scheme = Theme.of(context).colorScheme;
    return Semantics(
      liveRegion: true,
      child: Container(
        margin: const EdgeInsets.only(bottom: 12),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLow,
          borderRadius: BorderRadius.circular(10),
        ),
        child: Row(
          children: [
            Icon(Icons.cloud_off_rounded, size: 17, color: scheme.primary),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                context.tr('offlineCached'),
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: scheme.onSurface,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class Notice extends StatelessWidget {
  const Notice(this.text, {super.key, this.error = false});
  final String text;
  final bool error;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final accent = error ? scheme.error : scheme.primary;
    return Semantics(
      liveRegion: true,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 6),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        decoration: BoxDecoration(
          color: accent.withValues(alpha: 0.14),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              error ? Icons.error_outline_rounded : Icons.check_circle_rounded,
              size: 18,
              color: accent,
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                text,
                style: Theme.of(context).textTheme.bodyMedium
                    ?.copyWith(fontSize: 13.5),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Explicit Source Attribution Pill (`Nguồn: YouTube` / `Nguồn: TikTok` / `Nguồn: SoundCloud`)
class ProviderBadge extends StatelessWidget {
  const ProviderBadge({
    super.key,
    required this.provider,
    this.creator,
    this.showSourcePrefix = true,
  });

  final String provider;
  final String? creator;
  final bool showSourcePrefix;

  static Color accentFor(String provider) => switch (provider) {
    'soundcloud' => const Color(0xFFFF5500),
    'tiktok' => const Color(0xFF00D2BE),
    _ => const Color(0xFFFF4E45),
  };

  static IconData iconFor(String provider) => switch (provider) {
    'soundcloud' => Icons.cloud_queue_rounded,
    'tiktok' => Icons.music_note_rounded,
    _ => Icons.play_circle_fill_rounded,
  };

  @override
  Widget build(BuildContext context) {
    final baseLabel = providerDisplayLabel(provider);
    final accent = accentFor(provider);
    final parts = <String>[
      showSourcePrefix ? 'Nguồn: $baseLabel' : baseLabel,
      if (creator?.trim().isNotEmpty == true) creator!.trim(),
    ];
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
      decoration: BoxDecoration(
        color: accent.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(iconFor(provider), size: 13.5, color: accent),
          const SizedBox(width: 5),
          Flexible(
            child: Text(
              parts.join(' • '),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: accent,
                fontWeight: FontWeight.w700,
                fontSize: 11.5,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class CapabilityBadge extends StatelessWidget {
  const CapabilityBadge({super.key, required this.capability});
  final String capability;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final (icon, textKey, color) = switch (capability) {
      'EMBED_SUPPORTED' => (
        Icons.verified_rounded,
        'sourceVerified',
        TMusicTheme.spotifyGreen,
      ),
      'UNAVAILABLE' => (Icons.block_rounded, 'sourceUnavailable', scheme.error),
      _ => (
        Icons.check_circle_outline_rounded,
        'externalOnly',
        TMusicTheme.spotifyGreen,
      ),
    };
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 14.5, color: color),
        const SizedBox(width: 5),
        Flexible(
          child: Text(
            context.tr(textKey),
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: color,
              fontWeight: FontWeight.w600,
              fontSize: 12,
            ),
          ),
        ),
      ],
    );
  }
}

class PageList extends StatelessWidget {
  const PageList({
    super.key,
    required this.children,
    this.padding = const EdgeInsets.fromLTRB(16, 12, 16, 40),
  });
  final List<Widget> children;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) => Align(
    alignment: Alignment.topCenter,
    child: ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 760),
      child: ListView(padding: padding, children: children),
    ),
  );
}

class EmptyView extends StatelessWidget {
  const EmptyView({
    super.key,
    required this.title,
    required this.body,
    this.action,
    this.icon = Icons.library_music_outlined,
  });
  final String title, body;
  final Widget? action;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 34, horizontal: 8),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        TintedIconBadge(icon: icon, size: 60, iconSize: 28),
        const SizedBox(height: 14),
        Text(
          title,
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 6),
        Text(
          body,
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodyMedium
              ?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant),
        ),
        if (action != null) ...[const SizedBox(height: 18), action!],
      ],
    ),
  );
}

class LoadFailure extends StatelessWidget {
  const LoadFailure({super.key, required this.error, required this.retry});
  final Object error;
  final VoidCallback retry;

  @override
  Widget build(BuildContext context) => PageList(
    children: [
      Notice(errorText(context, error), error: true),
      const SizedBox(height: 8),
      OutlinedButton.icon(
        onPressed: retry,
        icon: const Icon(Icons.refresh_rounded),
        label: Text(context.tr('retry')),
      ),
    ],
  );
}

final _coverMemoryCache = <String, Uint8List>{};

class Cover extends ConsumerStatefulWidget {
  const Cover({
    super.key,
    this.url,
    this.size = 56,
    this.radius = 6,
    this.label = '',
  });
  final String? url;
  final double size;
  final double radius;
  final String label;

  static void evict(String? url) {
    if (url != null) _coverMemoryCache.remove(url);
  }

  @override
  ConsumerState<Cover> createState() => _CoverState();
}

class _CoverState extends ConsumerState<Cover> {
  Future<Uint8List>? _privateBytes;

  @override
  void initState() {
    super.initState();
    _preparePrivateCover();
  }

  @override
  void didUpdateWidget(covariant Cover oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.url != widget.url) {
      _privateBytes = null;
      _preparePrivateCover();
    }
  }

  void _preparePrivateCover() {
    final u = widget.url;
    if (u != null && u.startsWith('/api/v1/covers/')) {
      final cached = _coverMemoryCache[u];
      if (cached != null) {
        _privateBytes = Future.value(cached);
      } else {
        _privateBytes = ref.read(authorizedApiProvider).fetchBytes(u).then((
          bytes,
        ) {
          if (_coverMemoryCache.length > 64) {
            _coverMemoryCache.remove(_coverMemoryCache.keys.first);
          }
          _coverMemoryCache[u] = bytes;
          return bytes;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final u = widget.url;
    final presetColor = switch (u) {
      'cover:navy' || 'avatar:navy' => const Color(0xFF1F3864),
      'cover:blue' || 'avatar:blue' => const Color(0xFF2D46B9),
      'cover:green' || 'avatar:green' => const Color(0xFF1DB954),
      'cover:terracotta' || 'avatar:terracotta' => const Color(0xFFA74734),
      'cover:slate' || 'avatar:slate' => const Color(0xFF475569),
      'cover:amber' || 'avatar:amber' => const Color(0xFF9A671F),
      _ => null,
    };

    final fallback = Container(
      width: widget.size,
      height: widget.size,
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: presetColor != null
              ? [presetColor, presetColor.withValues(alpha: 0.72)]
              : const [Color(0xFF282828), Color(0xFF1A1A1A)],
        ),
      ),
      alignment: Alignment.center,
      child: Icon(
        Icons.music_note_rounded,
        size: (widget.size * 0.42).clamp(16, 54),
        color: Colors.white.withValues(alpha: 0.9),
      ),
    );

    Widget child = fallback;
    if (u != null && u.startsWith('/api/v1/covers/') && _privateBytes != null) {
      child = FutureBuilder<Uint8List>(
        future: _privateBytes,
        builder: (context, snapshot) {
          if (snapshot.hasData && snapshot.data!.isNotEmpty) {
            return Image.memory(
              snapshot.data!,
              width: widget.size,
              height: widget.size,
              fit: BoxFit.cover,
              cacheWidth: (widget.size * 3).round(),
              errorBuilder: (_, _, _) => fallback,
            );
          }
          return fallback;
        },
      );
    } else if (u != null) {
      final uri = Uri.tryParse(u);
      if (uri?.scheme == 'https') {
        child = Image.network(
          u,
          width: widget.size,
          height: widget.size,
          fit: BoxFit.cover,
          cacheWidth: (widget.size * 3).round(),
          errorBuilder: (_, _, _) => fallback,
        );
      }
    }

    final wrapped = ClipRRect(
      borderRadius: BorderRadius.circular(widget.radius),
      child: child,
    );
    return widget.label.isEmpty
        ? ExcludeSemantics(child: wrapped)
        : Semantics(label: widget.label, image: true, child: wrapped);
  }
}

Future<bool> confirmAction(
  BuildContext context,
  String title,
  String body,
) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: SingleChildScrollView(child: Text(body)),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: Text(context.tr('cancel')),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(context.tr('confirm')),
          ),
        ],
      ),
    ) ??
    false;

String? requiredText(BuildContext context, String? value) =>
    (value ?? '').trim().isEmpty ? context.tr('required') : null;
