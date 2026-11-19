import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../shared/models/library_models.dart';
import '../library/library_repository.dart';
import 'local_audio.dart';

final downloadJobsProvider =
    AsyncNotifierProvider<DownloadJobsController, List<AudioDownloadJob>>(
      DownloadJobsController.new,
      retry: (_, _) => null,
    );

final activeDownloadCountProvider = Provider<int>((ref) {
  final jobsAsync = ref.watch(downloadJobsProvider);
  if (!jobsAsync.hasValue) return 0;
  return (jobsAsync.value ?? const []).where((j) => j.isActive).length;
});

final songDownloadJobProvider = Provider.family<AudioDownloadJob?, String>((
  ref,
  songId,
) {
  final jobsAsync = ref.watch(downloadJobsProvider);
  if (!jobsAsync.hasValue) return null;
  for (final job in jobsAsync.value ?? const <AudioDownloadJob>[]) {
    if (job.songId == songId) return job;
  }
  return null;
});

class DownloadJobsController extends AsyncNotifier<List<AudioDownloadJob>> {
  Timer? _pollTimer;
  final Set<String> _syncingSongIds = <String>{};
  int _generation = 0;
  int _requestSequence = 0;
  String? _ownerId;
  bool _initialized = false;

  bool _current(int generation, String? ownerId) =>
      ref.mounted &&
      generation == _generation &&
      ref.read(sessionProvider).value?.userId == ownerId;

  @override
  Future<List<AudioDownloadJob>> build() async {
    final session = ref.watch(
      sessionProvider.select((value) => (value.hasValue, value.value?.userId)),
    );
    ref.watch(libraryRevisionProvider);
    final generation = ++_generation;
    final ownerId = session.$2;
    _pollTimer?.cancel();
    ref.onDispose(() {
      _generation++;
      _pollTimer?.cancel();
    });
    if (_initialized && _ownerId != ownerId) {
      // AsyncLoading preserves its previous value; replace it with an empty
      // snapshot first so an account switch cannot retain another user's jobs.
      state = const AsyncData([]);
      state = const AsyncLoading();
    }
    _initialized = true;
    _ownerId = ownerId;
    if (!session.$1) return const [];
    final repo = ref.read(libraryRepositoryProvider);
    final page = await repo.downloadJobs();
    if (!_current(generation, ownerId)) return const [];
    _schedulePolling(page.items, generation, ownerId);
    unawaited(_syncCompletedToDevice(page.items, repo, generation, ownerId));
    return page.items;
  }

  Future<void> refresh({bool silent = false}) async {
    final generation = _generation;
    final requestSequence = ++_requestSequence;
    final ownerId = ref.read(sessionProvider).value?.userId;
    final repo = ref.read(libraryRepositoryProvider);
    _pollTimer?.cancel();
    if (!silent) state = const AsyncLoading();
    try {
      final page = await repo.downloadJobs();
      if (!_current(generation, ownerId) ||
          requestSequence != _requestSequence) {
        return;
      }
      state = AsyncData(page.items);
      _schedulePolling(page.items, generation, ownerId);
      await _syncCompletedToDevice(page.items, repo, generation, ownerId);
    } catch (e, st) {
      if (!_current(generation, ownerId) ||
          requestSequence != _requestSequence) {
        return;
      }
      if (!silent) state = AsyncError(e, st);
    }
  }

  void trackJob(AudioDownloadJob job, {required String? ownerId}) {
    final generation = _generation;
    if (!_current(generation, ownerId)) return;
    final current = state.hasValue
        ? List<AudioDownloadJob>.from(state.value ?? const [])
        : <AudioDownloadJob>[];
    final idx = current.indexWhere((x) => x.id == job.id);
    if (idx >= 0) {
      current[idx] = job;
    } else {
      current.insert(0, job);
    }
    state = AsyncData(current);
    _schedulePolling(current, generation, ownerId);
  }

  void _schedulePolling(
    List<AudioDownloadJob> jobs,
    int generation,
    String? ownerId,
  ) {
    if (!_current(generation, ownerId)) return;
    _pollTimer?.cancel();
    if (jobs.any((j) => j.isActive)) {
      _pollTimer = Timer(const Duration(milliseconds: 1400), () {
        if (_current(generation, ownerId)) refresh(silent: true);
      });
    }
  }

  Future<void> _syncCompletedToDevice(
    List<AudioDownloadJob> jobs,
    LibraryRepository repo,
    int generation,
    String? ownerId,
  ) async {
    try {
      if (!_current(generation, ownerId)) return;
      final local = await ref.read(localLibraryProvider.future);
      if (!_current(generation, ownerId)) return;
      var syncedAny = false;
      for (final j in jobs) {
        if (!_current(generation, ownerId)) return;
        final syncKey = '$generation:${j.songId}';
        if (j.audioReady &&
            !_syncingSongIds.contains(syncKey) &&
            !await local.hasDownloadedSong(j.songId)) {
          if (!_current(generation, ownerId)) return;
          if (_syncingSongIds.contains(syncKey)) continue;
          _syncingSongIds.add(syncKey);
          try {
            final song = await repo.song(j.songId);
            if (!_current(generation, ownerId)) return;
            final bytes = await repo.fetchSongAudioBytes(j.songId);
            if (!_current(generation, ownerId)) return;
            await local.saveDownloadedSongMp3(song, bytes);
            if (!_current(generation, ownerId)) return;
            syncedAny = true;
          } finally {
            _syncingSongIds.remove(syncKey);
          }
        }
      }
      if (syncedAny && _current(generation, ownerId)) {
        TMusicHaptics.medium();
        ref.read(libraryRevisionProvider.notifier).changed();
      }
    } catch (_) {}
  }
}

class DownloadsScreen extends ConsumerStatefulWidget {
  const DownloadsScreen({super.key});

  @override
  ConsumerState<DownloadsScreen> createState() => _DownloadsScreenState();
}

class _DownloadsScreenState extends ConsumerState<DownloadsScreen> {
  Object? _actionError;
  bool _acting = false;

  Future<void> _playCompletedJob(AudioDownloadJob job) async {
    setState(() {
      _acting = true;
      _actionError = null;
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      final song = await repo.song(job.songId);
      final local = await ref.read(localLibraryProvider.future);
      final track = await local.ensureSongTrack(
        song,
        () => repo.fetchSongAudioBytes(song.id),
      );
      final player = await ref.read(localAudioProvider.future);
      TMusicHaptics.light();
      await player.start([track], 0);
    } catch (e) {
      if (mounted) setState(() => _actionError = e);
    } finally {
      if (mounted) setState(() => _acting = false);
    }
  }

  Future<void> _retryJob(AudioDownloadJob job) async {
    final ownerId = ref.read(sessionProvider).value?.userId;
    setState(() {
      _acting = true;
      _actionError = null;
    });
    try {
      final updated = await ref
          .read(libraryRepositoryProvider)
          .retryDownloadJob(job.id);
      ref
          .read(downloadJobsProvider.notifier)
          .trackJob(updated, ownerId: ownerId);
    } catch (e) {
      if (mounted) setState(() => _actionError = e);
    } finally {
      if (mounted) setState(() => _acting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(libraryRevisionProvider, (_, _) {
      ref.read(downloadJobsProvider.notifier).refresh(silent: true);
    });
    final jobsAsync = ref.watch(downloadJobsProvider);
    final tokens = Theme.of(context).extension<TMusicTokens>();
    final scheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(context.tr('downloadsTitle')),
        actions: [
          IconButton(
            tooltip: context.tr('offlineAudio'),
            onPressed: () => context.push('/local'),
            icon: const Icon(Icons.folder_outlined),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.read(downloadJobsProvider.notifier).refresh(),
        child: PageList(
          children: [
            const OfflineBanner(),
            Material(
              color: scheme.surface,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(18),
                side: BorderSide(
                  color: scheme.outlineVariant.withValues(alpha: 0.65),
                ),
              ),
              clipBehavior: Clip.antiAlias,
              child: ListTile(
                contentPadding: const EdgeInsets.symmetric(
                  horizontal: 14,
                  vertical: 6,
                ),
                leading: const TintedIconBadge(
                  icon: Icons.headphones_rounded,
                  size: 44,
                  iconSize: 22,
                ),
                title: Text(
                  context.tr('offlineAudio'),
                  style: Theme.of(context).textTheme.titleMedium
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                subtitle: Text(context.tr('viewSavedFiles')),
                trailing: const Icon(Icons.chevron_right_rounded),
                onTap: () => context.push('/local'),
              ),
            ),
            const SizedBox(height: 16),
            if (_acting || jobsAsync.isLoading) const LinearProgressIndicator(),
            if (_actionError != null)
              Notice(errorText(context, _actionError!), error: true),
            if (jobsAsync.hasError) ...[
              Notice(errorText(context, jobsAsync.error!), error: true),
              OutlinedButton(
                onPressed: () =>
                    ref.read(downloadJobsProvider.notifier).refresh(),
                child: Text(context.tr('retry')),
              ),
            ],
            if (jobsAsync.hasValue) ...[
              if ((jobsAsync.value ?? const []).isEmpty)
                EmptyView(
                  icon: Icons.downloading_rounded,
                  title: context.tr('emptyDownloads'),
                  body: context.tr('emptyDownloadsBody'),
                  action: FilledButton.icon(
                    onPressed: () => context.go('/'),
                    icon: const Icon(Icons.download_rounded),
                    label: Text(context.tr('downloadMp3Primary')),
                  ),
                ),
              ...(jobsAsync.value ?? const <AudioDownloadJob>[]).map((job) {
                final statusLabel = switch (job.status) {
                  'COMPLETE' => context.tr('mp3ReadyBadge'),
                  'RUNNING' || 'QUEUED' =>
                    '${context.tr('downloadingMp3')} (${job.progressPercent}%)',
                  'FAILED' => downloadFailureText(context, job.errorCode),
                  _ => job.status,
                };
                final statusColor = switch (job.status) {
                  'COMPLETE' => tokens?.success ?? scheme.primary,
                  'FAILED' => scheme.error,
                  _ => scheme.primary,
                };
                return Padding(
                  padding: const EdgeInsets.only(bottom: 10),
                  child: Material(
                    color: scheme.surface,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(18),
                      side: BorderSide(
                        color: scheme.outlineVariant.withValues(alpha: 0.6),
                      ),
                    ),
                    child: Padding(
                      padding: const EdgeInsets.all(14),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.center,
                            children: [
                              TintedIconBadge(
                                icon: job.audioReady
                                    ? Icons.music_note_rounded
                                    : Icons.downloading_rounded,
                                color: statusColor,
                                size: 44,
                                iconSize: 21,
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: InkWell(
                                  borderRadius: BorderRadius.circular(8),
                                  onTap: () =>
                                      context.push('/songs/${job.songId}'),
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        job.songTitle?.isNotEmpty == true
                                            ? job.songTitle!
                                            : job.canonicalUrl,
                                        style: Theme.of(context)
                                            .textTheme
                                            .titleMedium
                                            ?.copyWith(
                                              fontWeight: FontWeight.w700,
                                            ),
                                        maxLines: 2,
                                        overflow: TextOverflow.ellipsis,
                                      ),
                                      if (job.songArtist?.isNotEmpty ==
                                          true) ...[
                                        const SizedBox(height: 2),
                                        Text(
                                          job.songArtist!,
                                          style: Theme.of(context)
                                              .textTheme
                                              .bodySmall,
                                          maxLines: 1,
                                          overflow: TextOverflow.ellipsis,
                                        ),
                                      ],
                                    ],
                                  ),
                                ),
                              ),
                              if (job.audioReady)
                                IconButton(
                                  tooltip: context.tr('play'),
                                  onPressed: _acting
                                      ? null
                                      : () => _playCompletedJob(job),
                                  icon: Icon(
                                    Icons.play_circle_fill_rounded,
                                    size: 32,
                                    color: scheme.primary,
                                  ),
                                ),
                              IconButton(
                                visualDensity: VisualDensity.compact,
                                tooltip: context.tr(
                                  job.isActive ? 'cancel' : 'delete',
                                ),
                                onPressed: _acting
                                    ? null
                                    : () async {
                                        try {
                                          await ref
                                              .read(libraryRepositoryProvider)
                                              .cancelOrDeleteDownloadJob(
                                                job.id,
                                              );
                                          await ref
                                              .read(
                                                downloadJobsProvider.notifier,
                                              )
                                              .refresh(silent: true);
                                        } catch (e) {
                                          if (mounted) {
                                            setState(() => _actionError = e);
                                          }
                                        }
                                      },
                                icon: Icon(
                                  Icons.close_rounded,
                                  size: 19,
                                  color: scheme.onSurfaceVariant,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 10),
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 10,
                              vertical: 4,
                            ),
                            decoration: BoxDecoration(
                              color: statusColor.withValues(alpha: 0.12),
                              borderRadius: BorderRadius.circular(999),
                            ),
                            child: Text(
                              statusLabel,
                              style: Theme.of(context).textTheme.bodySmall
                                  ?.copyWith(
                                    color: statusColor,
                                    fontWeight: FontWeight.w700,
                                    fontSize: 11.5,
                                  ),
                            ),
                          ),
                          if (job.isActive) ...[
                            const SizedBox(height: 10),
                            SmoothProgressBar(percent: job.progressPercent),
                          ],
                          if (job.isRetryable) ...[
                            const SizedBox(height: 10),
                            OutlinedButton.icon(
                              onPressed: _acting ? null : () => _retryJob(job),
                              icon: const Icon(Icons.refresh_rounded, size: 18),
                              label: Text(context.tr('retry')),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                );
              }),
            ],
          ],
        ),
      ),
    );
  }
}
