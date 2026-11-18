import 'package:audio_service/audio_service.dart';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import 'local_audio.dart';

String formatPlayerClock(Duration d) {
  final totalSeconds = d.inSeconds < 0 ? 0 : d.inSeconds;
  final h = totalSeconds ~/ 3600;
  final m = (totalSeconds % 3600) ~/ 60;
  final s = totalSeconds % 60;
  final mm = m.toString().padLeft(h > 0 ? 2 : 1, '0');
  final ss = s.toString().padLeft(2, '0');
  return h > 0 ? '$h:$mm:$ss' : '$mm:$ss';
}

Future<void> openNowPlayingSheet(BuildContext context, LocalAudio player) {
  final reduce = MediaQuery.disableAnimationsOf(context);
  HapticFeedback.lightImpact();
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    showDragHandle: false,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
    ),
    clipBehavior: Clip.antiAlias,
    sheetAnimationStyle: reduce
        ? AnimationStyle.noAnimation
        : const AnimationStyle(
            duration: Duration(milliseconds: 270),
            reverseDuration: Duration(milliseconds: 200),
            curve: Curves.easeOutCubic,
            reverseCurve: Curves.easeInCubic,
          ),
    builder: (_) => FractionallySizedBox(
      heightFactor: 0.96,
      child: NowPlayingSheet(player: player),
    ),
  );
}

class LocalAudioScreen extends ConsumerStatefulWidget {
  const LocalAudioScreen({super.key});
  @override
  ConsumerState<LocalAudioScreen> createState() => _LocalAudioScreenState();
}

class _LocalAudioScreenState extends ConsumerState<LocalAudioScreen> {
  List<LocalTrack> _tracks = [];
  LocalAudio? _player;
  Object? _error;
  bool _busy = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final library = await ref.read(localLibraryProvider.future);
      final tracks = await library.list();
      if (mounted) setState(() => _tracks = tracks);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      if (mounted) setState(() => _error = null);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _import() async {
    if (!await confirmAction(
      context,
      context.tr('copyConsent'),
      context.tr('copyBody'),
    )) {
      return;
    }
    await _run(() async {
      final file = await openFile(
        acceptedTypeGroups: const [
          XTypeGroup(
            label: 'Audio',
            extensions: ['mp3', 'm4a', 'flac'],
            uniformTypeIdentifiers: ['public.audio'],
            mimeTypes: ['audio/mpeg', 'audio/mp4', 'audio/flac'],
          ),
        ],
      );
      if (file == null) return;
      await (await ref.read(localLibraryProvider.future)).importFile(file);
      await _load();
    });
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final globalPlayer = ref.watch(localAudioProvider);
    final activePlayer =
        _player ??
        (globalPlayer.hasValue && globalPlayer.value?.mediaItem.value != null
            ? globalPlayer.value
            : null);

    return Scaffold(
      appBar: AppBar(
        title: Text(context.tr('offlineAudio')),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 10),
            child: FilledButton.icon(
              style: FilledButton.styleFrom(
                visualDensity: VisualDensity.compact,
              ),
              onPressed: _busy ? null : _import,
              icon: const Icon(Icons.add_rounded, size: 19),
              label: Text(context.tr('chooseFile')),
            ),
          ),
        ],
      ),
      body: PageList(
        children: [
          if (_busy) const LinearProgressIndicator(),
          if (_error != null) ...[
            Notice(
              _error is FormatException
                  ? (_error! as FormatException).message
                  : errorText(context, _error!),
              error: true,
            ),
            OutlinedButton(onPressed: _load, child: Text(context.tr('retry'))),
          ],
          if (activePlayer != null) ...[
            LocalPlayerControls(activePlayer),
            const SizedBox(height: 16),
          ],
          if (!_busy && _tracks.isEmpty)
            EmptyView(
              icon: Icons.music_note_rounded,
              title: context.tr('noLocal'),
              body: context.tr('localNotice'),
              action: FilledButton.icon(
                onPressed: _busy ? null : _import,
                icon: const Icon(Icons.audio_file_outlined),
                label: Text(context.tr('chooseFile')),
              ),
            )
          else if (_tracks.isNotEmpty)
            Material(
              color: scheme.surface,
              borderRadius: BorderRadius.circular(12),
              clipBehavior: Clip.antiAlias,
              child: Column(
                children: [
                  for (final (index, track) in _tracks.indexed)
                    ListTile(
                      contentPadding: const EdgeInsets.symmetric(
                        horizontal: 14,
                        vertical: 4,
                      ),
                      leading: const TintedIconBadge(
                        icon: Icons.music_note_rounded,
                        size: 42,
                        iconSize: 20,
                      ),
                      title: Text(
                        track.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      subtitle: track.artist?.isNotEmpty == true
                          ? Text(
                              track.artist!,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            )
                          : null,
                      onTap: _busy
                          ? null
                          : () => _run(() async {
                              final player = await ref.read(
                                localAudioProvider.future,
                              );
                              await player.start(_tracks, index);
                              if (mounted) setState(() => _player = player);
                            }),
                      trailing: IconButton(
                        tooltip: context.tr('delete'),
                        icon: Icon(
                          Icons.delete_outline_rounded,
                          color: scheme.onSurfaceVariant,
                        ),
                        onPressed: _busy
                            ? null
                            : () => _run(() async {
                                await activePlayer?.stop();
                                await (await ref.read(
                                  localLibraryProvider.future,
                                )).remove(track);
                                await _load();
                              }),
                      ),
                    ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

/// Spotify Now Playing Controls:
/// Clean slim white/green seekbar + tabular timestamps + Shuffle / Prev / 64dp White Circle Play-Pause / Next / 3-State Repeat
/// + A–B Segment Loop & Playback Speed pills.
class LocalPlayerControls extends StatefulWidget {
  const LocalPlayerControls(
    this.player, {
    super.key,
    this.showHeader = true,
    this.allowExpandSheet = true,
  });

  final LocalAudio player;
  final bool showHeader;
  final bool allowExpandSheet;

  @override
  State<LocalPlayerControls> createState() => _LocalPlayerControlsState();
}

class _LocalPlayerControlsState extends State<LocalPlayerControls> {
  double? _dragMs;
  bool _showAbPanel = false;

  static const _speeds = <double>[0.8, 1.0, 1.25, 1.5];

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final textTheme = Theme.of(context).textTheme;
    const tabular = [FontFeature.tabularFigures()];

    final content = StreamBuilder<PlaybackState>(
      initialData: widget.player.playbackState.value,
      stream: widget.player.playbackState,
      builder: (context, snapshot) {
        final state = snapshot.data;
        final playing = state?.playing == true;
        final repeatMode = state?.repeatMode ?? AudioServiceRepeatMode.none;
        final shuffleOn = state?.shuffleMode == AudioServiceShuffleMode.all;

        final (repeatIcon, repeatLabelKey) = switch (repeatMode) {
          AudioServiceRepeatMode.one => (Icons.repeat_one_rounded, 'repeatOne'),
          AudioServiceRepeatMode.all ||
          AudioServiceRepeatMode.group => (Icons.repeat_rounded, 'repeatAll'),
          AudioServiceRepeatMode.none => (Icons.repeat_rounded, 'repeatOff'),
        };

        return StreamBuilder<MediaItem?>(
          initialData: widget.player.mediaItem.value,
          stream: widget.player.mediaItem,
          builder: (context, itemSnap) {
            final item = itemSnap.data;
            return Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (widget.showHeader)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: Row(
                      children: [
                        Cover(
                          url: item?.artUri?.toString(),
                          size: 46,
                          radius: 6,
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                item?.title ?? context.tr('nowPlaying'),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: textTheme.titleMedium?.copyWith(
                                  fontWeight: FontWeight.w700,
                                  color: TMusicTheme.spotifyGreen,
                                ),
                              ),
                              if (item?.artist?.isNotEmpty == true)
                                Text(
                                  item!.artist!,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: textTheme.bodySmall,
                                ),
                            ],
                          ),
                        ),
                        if (widget.allowExpandSheet)
                          IconButton(
                            key: const Key('expand-now-playing'),
                            tooltip: context.tr('expandPlayer'),
                            onPressed: () =>
                                openNowPlayingSheet(context, widget.player),
                            icon: const Icon(
                              Icons.open_in_full_rounded,
                              size: 19,
                            ),
                          ),
                        IconButton(
                          tooltip: context.tr('stop'),
                          onPressed: widget.player.stop,
                          icon: const Icon(Icons.close_rounded, size: 20),
                        ),
                      ],
                    ),
                  ),
                if (state?.errorMessage != null)
                  Notice(state!.errorMessage!, error: true),
                StreamBuilder<Duration?>(
                  initialData: widget.player.currentDuration,
                  stream: widget.player.duration,
                  builder: (context, durationSnap) => StreamBuilder<Duration>(
                    initialData: widget.player.currentPosition,
                    stream: widget.player.position,
                    builder: (context, positionSnap) {
                      final totalMs =
                          (durationSnap.data ?? widget.player.currentDuration)
                              ?.inMilliseconds ??
                          0;
                      final safeTotal = totalMs > 0 ? totalMs : 0;
                      final livePosMs =
                          (positionSnap.data ?? widget.player.currentPosition)
                              .inMilliseconds
                              .clamp(0, safeTotal);
                      final effectiveMs =
                          (_dragMs != null ? _dragMs!.round() : livePosMs)
                              .clamp(0, safeTotal);

                      return ValueListenableBuilder<
                        ({Duration start, Duration end})?
                      >(
                        valueListenable: widget.player.abLoop,
                        builder: (context, ab, _) {
                          return Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              SliderTheme(
                                data: SliderTheme.of(context).copyWith(
                                  trackHeight: 3.5,
                                  activeTrackColor: isDark
                                      ? Colors.white
                                      : TMusicTheme.spotifyGreen,
                                  thumbColor: isDark
                                      ? Colors.white
                                      : TMusicTheme.spotifyGreen,
                                  inactiveTrackColor: scheme.onSurface
                                      .withValues(alpha: 0.22),
                                  thumbShape: const RoundSliderThumbShape(
                                    enabledThumbRadius: 6,
                                  ),
                                  overlayShape: const RoundSliderOverlayShape(
                                    overlayRadius: 14,
                                  ),
                                ),
                                child: Slider(
                                  key: const Key('player-seek-slider'),
                                  value: effectiveMs.toDouble(),
                                  min: 0,
                                  max: safeTotal > 0
                                      ? safeTotal.toDouble()
                                      : 1,
                                  onChangeStart: safeTotal > 0
                                      ? (v) => setState(() => _dragMs = v)
                                      : null,
                                  onChanged: safeTotal > 0
                                      ? (v) => setState(() => _dragMs = v)
                                      : null,
                                  onChangeEnd: safeTotal > 0
                                      ? (v) async {
                                          setState(() => _dragMs = null);
                                          await widget.player.seek(
                                            Duration(milliseconds: v.round()),
                                          );
                                        }
                                      : null,
                                  semanticFormatterCallback: (v) =>
                                      formatPlayerClock(
                                        Duration(milliseconds: v.round()),
                                      ),
                                ),
                              ),
                              Padding(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 8,
                                ),
                                child: Wrap(
                                  alignment: WrapAlignment.spaceBetween,
                                  crossAxisAlignment: WrapCrossAlignment.center,
                                  spacing: 8,
                                  runSpacing: 4,
                                  children: [
                                    Text(
                                      formatPlayerClock(
                                        Duration(milliseconds: effectiveMs),
                                      ),
                                      key: const Key('player-pos-time'),
                                      style: textTheme.bodySmall?.copyWith(
                                        fontFeatures: tabular,
                                      ),
                                    ),
                                    if (ab != null)
                                      Container(
                                        padding: const EdgeInsets.symmetric(
                                          horizontal: 8,
                                          vertical: 2,
                                        ),
                                        decoration: BoxDecoration(
                                          color: TMusicTheme.spotifyGreen
                                              .withValues(alpha: 0.18),
                                          borderRadius: BorderRadius.circular(
                                            999,
                                          ),
                                        ),
                                        child: Text(
                                          'A–B ${formatPlayerClock(ab.start)} – ${formatPlayerClock(ab.end)}',
                                          style: textTheme.labelSmall?.copyWith(
                                            color: TMusicTheme.spotifyGreen,
                                            fontFeatures: tabular,
                                            fontWeight: FontWeight.w700,
                                          ),
                                        ),
                                      ),
                                    Text(
                                      formatPlayerClock(
                                        Duration(milliseconds: safeTotal),
                                      ),
                                      key: const Key('player-dur-time'),
                                      style: textTheme.bodySmall?.copyWith(
                                        fontFeatures: tabular,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              const SizedBox(height: 8),
                              // Exact Spotify 5-button Transport (Reference Screenshots 2 & 5)
                              Wrap(
                                alignment: WrapAlignment.spaceBetween,
                                crossAxisAlignment: WrapCrossAlignment.center,
                                children: [
                                  IconButton(
                                    key: const Key('player-rewind-10'),
                                    tooltip: context.tr('rewind10'),
                                    onPressed: () {
                                      HapticFeedback.selectionClick();
                                      widget.player.seekRelative(
                                        const Duration(seconds: -10),
                                      );
                                    },
                                    icon: Icon(
                                      Icons.replay_10_rounded,
                                      color: scheme.onSurfaceVariant,
                                    ),
                                  ),
                                  IconButton(
                                    key: const Key('player-prev'),
                                    tooltip: context.tr('previous'),
                                    onPressed: widget.player.skipToPrevious,
                                    iconSize: 34,
                                    icon: const Icon(
                                      Icons.skip_previous_rounded,
                                    ),
                                  ),
                                  SizedBox(
                                    width: 64,
                                    height: 64,
                                    child: Material(
                                      color: isDark
                                          ? Colors.white
                                          : Colors.black,
                                      shape: const CircleBorder(),
                                      child: InkWell(
                                        key: const Key('player-play-pause'),
                                        customBorder: const CircleBorder(),
                                        onTap: () {
                                          HapticFeedback.lightImpact();
                                          if (playing) {
                                            widget.player.pause();
                                          } else {
                                            widget.player.play();
                                          }
                                        },
                                        child: Semantics(
                                          label: context.tr(
                                            playing ? 'pause' : 'play',
                                          ),
                                          child: Center(
                                            child: Padding(
                                              padding: EdgeInsets.only(
                                                left: playing ? 0 : 2,
                                              ),
                                              child: Icon(
                                                playing
                                                    ? Icons.pause_rounded
                                                    : Icons.play_arrow_rounded,
                                                size: 36,
                                                color: isDark
                                                    ? Colors.black
                                                    : Colors.white,
                                              ),
                                            ),
                                          ),
                                        ),
                                      ),
                                    ),
                                  ),
                                  IconButton(
                                    key: const Key('player-next'),
                                    tooltip: context.tr('next'),
                                    onPressed: widget.player.skipToNext,
                                    iconSize: 34,
                                    icon: const Icon(Icons.skip_next_rounded),
                                  ),
                                  IconButton(
                                    key: const Key('player-forward-10'),
                                    tooltip: context.tr('forward10'),
                                    onPressed: () {
                                      HapticFeedback.selectionClick();
                                      widget.player.seekRelative(
                                        const Duration(seconds: 10),
                                      );
                                    },
                                    icon: Icon(
                                      Icons.forward_10_rounded,
                                      color: scheme.onSurfaceVariant,
                                    ),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 12),
                              // Spotify Secondary Mode Pills (Repeat Off/1/All · A-B Loop · Shuffle)
                              Wrap(
                                alignment: WrapAlignment.center,
                                spacing: 8,
                                runSpacing: 8,
                                children: [
                                  _SpotifyModePill(
                                    pillKey: const Key('player-repeat-cycle'),
                                    icon: repeatIcon,
                                    label: context.tr(repeatLabelKey),
                                    active:
                                        repeatMode !=
                                        AudioServiceRepeatMode.none,
                                    onTap: () {
                                      HapticFeedback.selectionClick();
                                      widget.player.cycleRepeatMode();
                                    },
                                  ),
                                  _SpotifyModePill(
                                    pillKey: const Key('player-ab-toggle'),
                                    icon: Icons.content_cut_rounded,
                                    label: context.tr('abLoopTitle'),
                                    active: ab != null || _showAbPanel,
                                    onTap: () {
                                      HapticFeedback.selectionClick();
                                      setState(
                                        () => _showAbPanel = !_showAbPanel,
                                      );
                                    },
                                  ),
                                  _SpotifyModePill(
                                    pillKey: const Key('player-shuffle-toggle'),
                                    icon: Icons.shuffle_rounded,
                                    label: context.tr('shuffle'),
                                    active: shuffleOn,
                                    onTap: () {
                                      HapticFeedback.selectionClick();
                                      widget.player.setShuffleMode(
                                        shuffleOn
                                            ? AudioServiceShuffleMode.none
                                            : AudioServiceShuffleMode.all,
                                      );
                                    },
                                  ),
                                ],
                              ),
                              if (_showAbPanel || ab != null) ...[
                                const SizedBox(height: 12),
                                _buildAbPanel(
                                  context,
                                  safeTotal,
                                  livePosMs,
                                  ab,
                                ),
                              ],
                            ],
                          );
                        },
                      );
                    },
                  ),
                ),
                const SizedBox(height: 12),
                ValueListenableBuilder<double>(
                  valueListenable: widget.player.speed,
                  builder: (context, currentSpeed, _) => Wrap(
                    alignment: WrapAlignment.center,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    spacing: 6,
                    runSpacing: 6,
                    children: [
                      Icon(
                        Icons.speed_rounded,
                        size: 16,
                        color: scheme.onSurfaceVariant,
                      ),
                      for (final rate in _speeds)
                        ChoiceChip(
                          key: Key('speed-$rate'),
                          showCheckmark: false,
                          visualDensity: VisualDensity.compact,
                          label: Text('${rate}x'),
                          selected: (currentSpeed - rate).abs() < 0.01,
                          onSelected: (_) {
                            HapticFeedback.selectionClick();
                            widget.player.changeSpeed(rate);
                          },
                        ),
                    ],
                  ),
                ),
              ],
            );
          },
        );
      },
    );

    if (!widget.showHeader) return content;

    return StreamBuilder<MediaItem?>(
      initialData: widget.player.mediaItem.value,
      stream: widget.player.mediaItem,
      builder: (context, snap) {
        if (snap.data == null) return const SizedBox.shrink();
        return Material(
          color: scheme.surfaceContainerLow,
          borderRadius: BorderRadius.circular(14),
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: content,
          ),
        );
      },
    );
  }

  Widget _buildAbPanel(
    BuildContext context,
    int safeTotalMs,
    int livePosMs,
    ({Duration start, Duration end})? ab,
  ) {
    final scheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;
    const tabular = [FontFeature.tabularFigures()];

    final canSlide = safeTotalMs >= 2000;
    final startMs = canSlide
        ? (ab?.start.inMilliseconds ?? 0).clamp(0, safeTotalMs - 1000)
        : 0;
    final endMs = canSlide
        ? (ab?.end.inMilliseconds ?? safeTotalMs).clamp(
            startMs + 1000,
            safeTotalMs,
          )
        : 1000;

    return Material(
      key: const Key('ab-loop-panel'),
      color: scheme.surfaceContainerLow,
      borderRadius: BorderRadius.circular(14),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Wrap(
              alignment: WrapAlignment.spaceBetween,
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: 8,
              runSpacing: 4,
              children: [
                Text(
                  ab != null
                      ? '${context.tr('abLoopActive')}: ${formatPlayerClock(ab.start)} – ${formatPlayerClock(ab.end)}'
                      : context.tr('abLoopTitle'),
                  style: textTheme.labelLarge?.copyWith(fontFeatures: tabular),
                ),
                if (ab != null)
                  TextButton.icon(
                    key: const Key('ab-clear'),
                    style: TextButton.styleFrom(
                      visualDensity: VisualDensity.compact,
                    ),
                    onPressed: () {
                      HapticFeedback.selectionClick();
                      widget.player.clearAbLoop();
                    },
                    icon: const Icon(Icons.close_rounded, size: 16),
                    label: Text(context.tr('clearAbLoop')),
                  ),
              ],
            ),
            if (canSlide)
              RangeSlider(
                key: const Key('ab-range-slider'),
                activeColor: TMusicTheme.spotifyGreen,
                values: RangeValues(startMs.toDouble(), endMs.toDouble()),
                min: 0,
                max: safeTotalMs.toDouble(),
                labels: RangeLabels(
                  formatPlayerClock(Duration(milliseconds: startMs)),
                  formatPlayerClock(Duration(milliseconds: endMs)),
                ),
                onChanged: (values) {
                  final s = values.start.round();
                  final e = values.end.round();
                  if (e - s >= 1000) {
                    widget.player.setAbLoop(
                      Duration(milliseconds: s),
                      Duration(milliseconds: e),
                    );
                  }
                },
              ),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                OutlinedButton.icon(
                  key: const Key('ab-set-a'),
                  style: OutlinedButton.styleFrom(
                    visualDensity: VisualDensity.compact,
                  ),
                  onPressed: () {
                    HapticFeedback.selectionClick();
                    final a = Duration(milliseconds: livePosMs);
                    final fallbackEnd = safeTotalMs > livePosMs + 1000
                        ? Duration(milliseconds: safeTotalMs)
                        : a + const Duration(seconds: 15);
                    final b =
                        (ab != null &&
                            ab.end > a + const Duration(seconds: 1))
                        ? ab.end
                        : fallbackEnd;
                    widget.player.setAbLoop(a, b);
                  },
                  icon: const Icon(Icons.first_page_rounded, size: 18),
                  label: Text(context.tr('setPointA')),
                ),
                OutlinedButton.icon(
                  key: const Key('ab-set-b'),
                  style: OutlinedButton.styleFrom(
                    visualDensity: VisualDensity.compact,
                  ),
                  onPressed: () {
                    HapticFeedback.selectionClick();
                    final b = Duration(milliseconds: livePosMs);
                    final a = (ab != null && ab.start < b)
                        ? ab.start
                        : Duration.zero;
                    if (b > a) {
                      widget.player.setAbLoop(a, b);
                    }
                  },
                  icon: const Icon(Icons.last_page_rounded, size: 18),
                  label: Text(context.tr('setPointB')),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _SpotifyModePill extends StatelessWidget {
  const _SpotifyModePill({
    this.pillKey,
    required this.icon,
    required this.label,
    required this.active,
    required this.onTap,
  });

  final Key? pillKey;
  final IconData icon;
  final String label;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final fg = active ? TMusicTheme.spotifyGreen : scheme.onSurfaceVariant;
    return Material(
      color: active
          ? TMusicTheme.spotifyGreen.withValues(alpha: 0.16)
          : scheme.surfaceContainerLow,
      shape: const StadiumBorder(),
      child: InkWell(
        key: pillKey,
        customBorder: const StadiumBorder(),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 17, color: fg),
              const SizedBox(width: 5),
              Flexible(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: fg,
                    fontWeight: active ? FontWeight.w700 : FontWeight.w600,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Authentic Spotify Full-Screen Now Playing Sheet (matching Reference Screenshots 2 & 5):
/// Dynamic Album-Toned Full-Bleed Gradient -> PLAYING FROM ALBUM header ->
/// Centered Square Cover -> Left-aligned Title/Artist + Green Check Badge -> Slim White Seekbar.
class NowPlayingSheet extends StatefulWidget {
  const NowPlayingSheet({super.key, required this.player});
  final LocalAudio player;

  @override
  State<NowPlayingSheet> createState() => _NowPlayingSheetState();
}

class _NowPlayingSheetState extends State<NowPlayingSheet> {
  bool _showQueue = false;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final tokens = Theme.of(context).extension<TMusicTokens>();
    final textTheme = Theme.of(context).textTheme;

    return StreamBuilder<MediaItem?>(
      initialData: widget.player.mediaItem.value,
      stream: widget.player.mediaItem,
      builder: (context, itemSnap) {
        final item = itemSnap.data;
        if (item == null) {
          return Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Text(context.tr('noLocal')),
            ),
          );
        }
        final ambient = albumAmbientColor(
          item.artUri?.toString() ?? item.id,
          isDark: isDark,
        );

        return DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topCenter,
              end: Alignment.bottomCenter,
              stops: const [0.0, 0.58, 1.0],
              colors: [
                ambient,
                isDark ? const Color(0xFF161616) : scheme.surface,
                isDark ? TMusicTheme.darkCanvas : scheme.surface,
              ],
            ),
          ),
          child: StreamBuilder<List<MediaItem>>(
            initialData: widget.player.queue.value,
            stream: widget.player.queue,
            builder: (context, queueSnap) {
              final queue = queueSnap.data ?? const [];
              return Column(
                children: [
                  const SizedBox(height: 8),
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    child: Row(
                      children: [
                        IconButton(
                          tooltip: context.tr('cancel'),
                          onPressed: () => Navigator.pop(context),
                          icon: const Icon(
                            Icons.keyboard_arrow_down_rounded,
                            size: 32,
                          ),
                        ),
                        Expanded(
                          child: Column(
                            children: [
                              Text(
                                context.tr('nowPlaying').toUpperCase(),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: textTheme.labelSmall?.copyWith(
                                  letterSpacing: 1.3,
                                ),
                              ),
                              if (item.album?.isNotEmpty == true)
                                Text(
                                  item.album!,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: textTheme.labelLarge,
                                ),
                            ],
                          ),
                        ),
                        IconButton(
                          key: const Key('now-playing-queue-toggle'),
                          tooltip: context.tr('upNextQueue'),
                          isSelected: _showQueue,
                          onPressed: () {
                            HapticFeedback.selectionClick();
                            setState(() => _showQueue = !_showQueue);
                          },
                          icon: Badge(
                            backgroundColor: TMusicTheme.spotifyGreen,
                            textColor: Colors.black,
                            isLabelVisible: queue.length > 1,
                            label: Text('${queue.length}'),
                            child: Icon(
                              Icons.queue_music_rounded,
                              color: _showQueue
                                  ? TMusicTheme.spotifyGreen
                                  : null,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  Expanded(
                    child: ListView(
                      padding: const EdgeInsets.fromLTRB(22, 10, 22, 24),
                      children: [
                        if (!_showQueue) ...[
                          Center(
                            child: GestureDetector(
                              onHorizontalDragEnd: (details) {
                                final v = details.primaryVelocity ?? 0;
                                if (v < -350) {
                                  HapticFeedback.mediumImpact();
                                  widget.player.skipToNext();
                                } else if (v > 350) {
                                  HapticFeedback.mediumImpact();
                                  widget.player.skipToPrevious();
                                }
                              },
                              child: DecoratedBox(
                                decoration: BoxDecoration(
                                  borderRadius: BorderRadius.circular(10),
                                  boxShadow: tokens?.heroShadow,
                                ),
                                child: Cover(
                                  url: item.artUri?.toString(),
                                  size: 204,
                                  radius: 10,
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 20),
                          // Spotify Left-Aligned Title + Artist + Green Check Circle (Screenshot #2)
                          Row(
                            children: [
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      item.title,
                                      maxLines: 2,
                                      overflow: TextOverflow.ellipsis,
                                      style: textTheme.headlineSmall?.copyWith(
                                        fontSize: 21,
                                      ),
                                    ),
                                    if (item.artist?.isNotEmpty == true) ...[
                                      const SizedBox(height: 3),
                                      Text(
                                        item.artist!,
                                        maxLines: 1,
                                        overflow: TextOverflow.ellipsis,
                                        style: textTheme.bodyMedium?.copyWith(
                                          color: scheme.onSurfaceVariant,
                                        ),
                                      ),
                                    ],
                                  ],
                                ),
                              ),
                              const SizedBox(width: 12),
                              Container(
                                width: 28,
                                height: 28,
                                decoration: const BoxDecoration(
                                  color: TMusicTheme.spotifyGreen,
                                  shape: BoxShape.circle,
                                ),
                                child: const Icon(
                                  Icons.check_rounded,
                                  size: 18,
                                  color: Colors.black,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 10),
                        ] else ...[
                          Text(
                            '${context.tr('upNextQueue')} (${queue.length})',
                            style: textTheme.titleMedium?.copyWith(
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: 10),
                          ConstrainedBox(
                            constraints: const BoxConstraints(maxHeight: 230),
                            child: Material(
                              color: scheme.surfaceContainerLow,
                              borderRadius: BorderRadius.circular(14),
                              clipBehavior: Clip.antiAlias,
                              child: ListView.builder(
                                shrinkWrap: true,
                                itemCount: queue.length,
                                itemBuilder: (context, i) {
                                  final q = queue[i];
                                  final active = q.id == item.id;
                                  return ListTile(
                                    dense: true,
                                    selected: active,
                                    leading: active
                                        ? const Icon(
                                            Icons.graphic_eq_rounded,
                                            color: TMusicTheme.spotifyGreen,
                                          )
                                        : Text(
                                            '${i + 1}',
                                            style: textTheme.bodySmall,
                                          ),
                                    title: Text(
                                      q.title,
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                        fontWeight: active
                                            ? FontWeight.w700
                                            : FontWeight.w500,
                                        color: active
                                            ? TMusicTheme.spotifyGreen
                                            : null,
                                      ),
                                    ),
                                    subtitle: q.artist?.isNotEmpty == true
                                        ? Text(
                                            q.artist!,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                          )
                                        : null,
                                    onTap: () {
                                      HapticFeedback.selectionClick();
                                      widget.player.skipToQueueItem(i);
                                    },
                                  );
                                },
                              ),
                            ),
                          ),
                          const SizedBox(height: 14),
                        ],
                        LocalPlayerControls(
                          widget.player,
                          showHeader: false,
                          allowExpandSheet: false,
                        ),
                      ],
                    ),
                  ),
                ],
              );
            },
          ),
        );
      },
    );
  }
}
