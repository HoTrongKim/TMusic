import 'dart:ui';

import 'package:audio_service/audio_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../features/playback/local_audio.dart';
import '../../features/playback/local_audio_screen.dart';
import '../localization/app_copy.dart';
import 'common.dart';

extension TMusicMotionContext on BuildContext {
  bool get reduceMotion => MediaQuery.disableAnimationsOf(this);
}

abstract final class TMusicHaptics {
  static void selection() => HapticFeedback.selectionClick();
  static void light() => HapticFeedback.lightImpact();
  static void medium() => HapticFeedback.mediumImpact();
}

/// Subtle Glassmorphism reserved exclusively for Mini-Player, Navigation Bar, and Floating Sheets.
class SpotifyGlassSurface extends StatelessWidget {
  const SpotifyGlassSurface({
    super.key,
    required this.child,
    this.borderRadius = BorderRadius.zero,
    this.tint,
    this.border,
  });

  final Widget child;
  final BorderRadius borderRadius;
  final Color? tint;
  final BoxBorder? border;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final bg =
        tint ??
        (isDark
            ? const Color(0xD9242424)
            : const Color(0xEBFFFFFF));
    final box = DecoratedBox(
      decoration: BoxDecoration(
        color: bg,
        borderRadius: borderRadius,
        border:
            border ??
            Border.all(
              color: (isDark ? Colors.white : Colors.black).withValues(
                alpha: 0.08,
              ),
            ),
      ),
      child: Material(type: MaterialType.transparency, child: child),
    );
    if (context.reduceMotion) {
      return ClipRRect(borderRadius: borderRadius, child: box);
    }
    return ClipRRect(
      borderRadius: borderRadius,
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 16, sigmaY: 16),
        child: box,
      ),
    );
  }
}

class PressableScale extends StatefulWidget {
  const PressableScale({
    super.key,
    required this.child,
    this.enabled = true,
    this.pressedScale = 0.97,
  });

  final Widget child;
  final bool enabled;
  final double pressedScale;

  @override
  State<PressableScale> createState() => _PressableScaleState();
}

class _PressableScaleState extends State<PressableScale> {
  bool _pressed = false;

  void _set(bool value) {
    if (_pressed != value && mounted) {
      setState(() => _pressed = value);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.enabled || context.reduceMotion) return widget.child;
    return Listener(
      behavior: HitTestBehavior.translucent,
      onPointerDown: (_) => _set(true),
      onPointerUp: (_) => _set(false),
      onPointerCancel: (_) => _set(false),
      child: AnimatedScale(
        scale: _pressed ? widget.pressedScale : 1.0,
        duration: Duration(milliseconds: _pressed ? 90 : 150),
        curve: Curves.easeOutCubic,
        child: widget.child,
      ),
    );
  }
}

class SmoothProgressBar extends StatelessWidget {
  const SmoothProgressBar({super.key, this.percent});
  final int? percent;

  @override
  Widget build(BuildContext context) {
    final p = percent;
    if (p == null || p <= 0) {
      return ClipRRect(
        borderRadius: BorderRadius.circular(999),
        child: const LinearProgressIndicator(minHeight: 4.5),
      );
    }
    final target = (p / 100.0).clamp(0.02, 1.0);
    if (context.reduceMotion) {
      return ClipRRect(
        borderRadius: BorderRadius.circular(999),
        child: LinearProgressIndicator(value: target, minHeight: 4.5),
      );
    }
    return TweenAnimationBuilder<double>(
      tween: Tween<double>(begin: 0, end: target),
      duration: const Duration(milliseconds: 360),
      curve: Curves.easeOutCubic,
      builder: (context, value, _) => ClipRRect(
        borderRadius: BorderRadius.circular(999),
        child: LinearProgressIndicator(value: value, minHeight: 4.5),
      ),
    );
  }
}

Future<T?> showTMusicSheet<T>(
  BuildContext context, {
  required WidgetBuilder builder,
}) {
  final reduce = context.reduceMotion;
  return showModalBottomSheet<T>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    showDragHandle: true,
    backgroundColor: Colors.transparent,
    sheetAnimationStyle: reduce
        ? AnimationStyle.noAnimation
        : const AnimationStyle(
            duration: Duration(milliseconds: 250),
            reverseDuration: Duration(milliseconds: 190),
            curve: Curves.easeOutCubic,
            reverseCurve: Curves.easeInCubic,
          ),
    builder: (ctx) => SpotifyGlassSurface(
      borderRadius: const BorderRadius.vertical(top: Radius.circular(22)),
      tint: Theme.of(ctx).brightness == Brightness.dark
          ? const Color(0xEB1E1E1E)
          : const Color(0xF2FFFFFF),
      child: builder(ctx),
    ),
  );
}

/// Authentic Spotify Floating Glass Mini-Player (matching Reference Screenshots 3, 4, 5, 6):
/// Rounded-8dp dark glass bar floating right above Bottom Nav, 40x40 square cover,
/// crisp Title + Artist, Repeat toggle, Play/Pause, and razor-thin 2dp progress bar along bottom edge.
class MiniPlayerDock extends ConsumerWidget {
  const MiniPlayerDock({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (MediaQuery.viewInsetsOf(context).bottom > 0) {
      return const SizedBox.shrink();
    }
    final playerAsync = ref.watch(localAudioProvider);
    final player = playerAsync.hasValue ? playerAsync.value : null;
    if (player == null) return const SizedBox.shrink();

    return StreamBuilder<MediaItem?>(
      initialData: player.mediaItem.value,
      stream: player.mediaItem,
      builder: (context, itemSnap) {
        final item = itemSnap.data;
        if (item == null) return const SizedBox.shrink();
        final scheme = Theme.of(context).colorScheme;
        final isDark = Theme.of(context).brightness == Brightness.dark;
        final tokens = Theme.of(context).extension<TMusicTokens>();
        final ambientTint = albumAmbientColor(
          item.artUri?.toString() ?? item.id,
          isDark: isDark,
        );

        return StreamBuilder<PlaybackState>(
          initialData: player.playbackState.value,
          stream: player.playbackState,
          builder: (context, stateSnap) {
            final state = stateSnap.data;
            final playing = state?.playing == true;
            final repeat = state?.repeatMode ?? AudioServiceRepeatMode.none;
            final repeatIcon = repeat == AudioServiceRepeatMode.one
                ? Icons.repeat_one_rounded
                : Icons.repeat_rounded;
            final repeatTooltip = switch (repeat) {
              AudioServiceRepeatMode.one => context.tr('repeatOne'),
              AudioServiceRepeatMode.all ||
              AudioServiceRepeatMode.group => context.tr('repeatAll'),
              AudioServiceRepeatMode.none => context.tr('repeatOff'),
            };

            return Padding(
              padding: const EdgeInsets.fromLTRB(8, 0, 8, 4),
              child: DecoratedBox(
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(8),
                  boxShadow: tokens?.floatingShadow,
                ),
                child: SpotifyGlassSurface(
                  borderRadius: BorderRadius.circular(8),
                  tint: isDark
                      ? Color.alphaBlend(
                          ambientTint.withValues(alpha: 0.42),
                          const Color(0xE0222222),
                        )
                      : const Color(0xF0FFFFFF),
                  child: Material(
                    color: Colors.transparent,
                    child: InkWell(
                      key: const Key('mini-player-dock'),
                      onTap: () => openNowPlayingSheet(context, player),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Padding(
                            padding: const EdgeInsets.fromLTRB(8, 6, 4, 5),
                            child: Row(
                              children: [
                                Cover(
                                  url: item.artUri?.toString(),
                                  size: 40,
                                  radius: 5,
                                ),
                                const SizedBox(width: 10),
                                Expanded(
                                  child: ValueListenableBuilder<
                                    ({Duration start, Duration end})?
                                  >(
                                    valueListenable: player.abLoop,
                                    builder: (context, ab, _) {
                                      final subParts = <String>[
                                        if (item.artist?.isNotEmpty == true)
                                          item.artist!,
                                        if (repeat ==
                                            AudioServiceRepeatMode.one)
                                          context.tr('repeatOne')
                                        else if (repeat !=
                                            AudioServiceRepeatMode.none)
                                          context.tr('repeatAll'),
                                        if (ab != null)
                                          'A–B ${formatPlayerClock(ab.start)}–${formatPlayerClock(ab.end)}',
                                      ];
                                      return Column(
                                        mainAxisSize: MainAxisSize.min,
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        children: [
                                          Text(
                                            item.title,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: Theme.of(context)
                                                .textTheme
                                                .titleMedium
                                                ?.copyWith(
                                                  fontSize: 13.5,
                                                  fontWeight: FontWeight.w700,
                                                ),
                                          ),
                                          if (subParts.isNotEmpty)
                                            Text(
                                              subParts.join(' • '),
                                              maxLines: 1,
                                              overflow: TextOverflow.ellipsis,
                                              style: Theme.of(context)
                                                  .textTheme
                                                  .bodySmall
                                                  ?.copyWith(fontSize: 11.5),
                                            ),
                                        ],
                                      );
                                    },
                                  ),
                                ),
                                IconButton(
                                  key: const Key('mini-player-repeat'),
                                  visualDensity: VisualDensity.compact,
                                  tooltip: repeatTooltip,
                                  onPressed: () {
                                    TMusicHaptics.selection();
                                    player.cycleRepeatMode();
                                  },
                                  icon: Icon(
                                    repeatIcon,
                                    size: 20,
                                    color: repeat != AudioServiceRepeatMode.none
                                        ? TMusicTheme.spotifyGreen
                                        : scheme.onSurfaceVariant,
                                  ),
                                ),
                                IconButton(
                                  key: const Key('mini-player-play-pause'),
                                  visualDensity: VisualDensity.compact,
                                  tooltip: context.tr(
                                    playing ? 'pause' : 'play',
                                  ),
                                  onPressed: () {
                                    TMusicHaptics.light();
                                    if (playing) {
                                      player.pause();
                                    } else {
                                      player.play();
                                    }
                                  },
                                  icon: Icon(
                                    playing
                                        ? Icons.pause_rounded
                                        : Icons.play_arrow_rounded,
                                    size: 28,
                                    color: scheme.onSurface,
                                  ),
                                ),
                                IconButton(
                                  visualDensity: VisualDensity.compact,
                                  tooltip: context.tr('stop'),
                                  onPressed: player.stop,
                                  icon: Icon(
                                    Icons.close_rounded,
                                    size: 18,
                                    color: scheme.onSurfaceVariant,
                                  ),
                                ),
                              ],
                            ),
                          ),
                          // Spotify's signature razor-thin bottom progress line
                          StreamBuilder<Duration?>(
                            initialData: player.currentDuration,
                            stream: player.duration,
                            builder: (context, durSnap) =>
                                StreamBuilder<Duration>(
                                  initialData: player.currentPosition,
                                  stream: player.position,
                                  builder: (context, posSnap) {
                                    final total =
                                        (durSnap.data ?? player.currentDuration)
                                            ?.inMilliseconds ??
                                        0;
                                    final pos =
                                        (posSnap.data ?? player.currentPosition)
                                            .inMilliseconds;
                                    final ratio = total > 0
                                        ? (pos / total).clamp(0.0, 1.0)
                                        : 0.0;
                                    return Padding(
                                      padding: const EdgeInsets.symmetric(
                                        horizontal: 8,
                                      ),
                                      child: LinearProgressIndicator(
                                        value: ratio,
                                        minHeight: 2,
                                        backgroundColor: scheme.onSurface
                                            .withValues(alpha: 0.16),
                                        valueColor:
                                            AlwaysStoppedAnimation<Color>(
                                              isDark
                                                  ? Colors.white
                                                  : TMusicTheme.spotifyGreen,
                                            ),
                                      ),
                                    );
                                  },
                                ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            );
          },
        );
      },
    );
  }
}
