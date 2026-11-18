import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/api/api_client.dart';
import '../../core/auth/external_auth.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../shared/models/library_models.dart';
import '../library/library_repository.dart';
import '../playback/local_audio.dart';
import '../playback/local_audio_screen.dart';
import '../share/shared_links.dart';

/// Music-First Song Detail & Playback Stage Screen
class SongScreen extends ConsumerStatefulWidget {
  const SongScreen({super.key, required this.id});
  final String id;

  @override
  ConsumerState<SongScreen> createState() => _SongScreenState();
}

class _SongScreenState extends ConsumerState<SongScreen> {
  Song? _song;
  List<Album> _memberAlbums = [];
  Object? _error;
  bool _busy = true;
  bool _hasLocalMp3 = false;
  LocalAudio? _player;
  AudioDownloadJob? _activeJob;
  Timer? _jobTimer;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _jobTimer?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    if (!mounted) return;
    var hasLocal = false;
    try {
      final local = await ref.read(localLibraryProvider.future);
      hasLocal = await local.hasDownloadedSong(widget.id);
    } catch (_) {}
    if (!mounted) return;
    try {
      final repo = ref.read(libraryRepositoryProvider);
      final s = await repo.song(widget.id);
      if (!s.isGuestLocal && s.audioReady && !hasLocal) {
        try {
          final local = await ref.read(localLibraryProvider.future);
          final bytes = await repo.fetchSongAudioBytes(s.id);
          await local.saveDownloadedSongMp3(s, bytes);
          hasLocal = true;
        } catch (_) {}
      }
      List<Album> memberAlbums = [];
      if (s.albumIds.isNotEmpty) {
        try {
          final allAlbums = await repo.albums();
          final set = s.albumIds.toSet();
          memberAlbums = allAlbums.items
              .where((a) => set.contains(a.id))
              .toList();
        } catch (_) {}
      }
      if (mounted) {
        setState(() {
          _song = s;
          _hasLocalMp3 = hasLocal;
          _memberAlbums = memberAlbums;
          _error = null;
        });
      }
      if (mounted &&
          !s.isGuestLocal &&
          (s.audioStatus == 'QUEUED' || s.audioStatus == 'RUNNING')) {
        _resumePollingForSong(s);
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _resumePollingForSong(Song s) async {
    try {
      final jobs = await ref.read(libraryRepositoryProvider).downloadJobs();
      final match = jobs.items
          .where((j) => j.songId == s.id && j.isActive)
          .firstOrNull;
      if (match != null) _pollJob(s, match.id);
    } catch (_) {}
  }

  void _pollJob(Song s, String jobId) {
    _jobTimer?.cancel();
    Future<void> check() async {
      try {
        final repo = ref.read(libraryRepositoryProvider);
        final job = await repo.downloadJob(jobId);
        if (!mounted) return;
        setState(() => _activeJob = job);
        if (job.isActive) {
          _jobTimer = Timer(const Duration(milliseconds: 1400), check);
        } else if (job.audioReady) {
          ref.read(libraryRevisionProvider.notifier).changed();
          await _load();
        }
      } catch (_) {}
    }

    check();
  }

  Future<void> _startMp3Download(Song s) async {
    if (!await confirmAction(
          context,
          context.tr('downloadMp3Button'),
          context.tr('sourceNotice'),
        ) ||
        !mounted) {
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final job = await ref
          .read(libraryRepositoryProvider)
          .startSongDownload(s.id);
      if (mounted) setState(() => _activeJob = job);
      if (job.isActive) {
        _pollJob(s, job.id);
      } else if (job.audioReady) {
        await _load();
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _playMp3(Song s) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final local = await ref.read(localLibraryProvider.future);
      final track = s.isGuestLocal
          ? await local.getDownloadedSongTrack(s)
          : await local.ensureSongTrack(
              s,
              () =>
                  ref.read(libraryRepositoryProvider).fetchSongAudioBytes(s.id),
            );
      if (track == null) {
        if (mounted) {
          setState(() => _hasLocalMp3 = false);
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text(context.tr('emptyDownloaded'))),
          );
        }
        return;
      }
      if (!mounted) return;
      final player = await ref.read(localAudioProvider.future);
      await player.start([track], 0);
      if (mounted) {
        setState(() {
          _player = player;
          _hasLocalMp3 = true;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _openSource(Uri url) async {
    try {
      if (!await launchUrl(url, mode: LaunchMode.externalApplication)) {
        throw const ApiFailure('source_unavailable');
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(errorText(context, e))));
      }
    }
  }

  Future<void> _toggleFavoriteOptimistic(Song current) async {
    final optimistic = current.copyWith(favorite: !current.favorite);
    setState(() {
      _song = optimistic;
      _error = null;
    });
    try {
      final updated = await ref
          .read(libraryRepositoryProvider)
          .favorite(current);
      ref.read(libraryRevisionProvider.notifier).changed();
      if (mounted) setState(() => _song = updated);
    } catch (e) {
      if (mounted) {
        setState(() {
          _song = current;
          _error = e;
        });
      }
    }
  }

  Future<void> _act(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.read(libraryRevisionProvider.notifier).changed();
      await _load();
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = _song;
    final scheme = Theme.of(context).colorScheme;
    final tokens = Theme.of(context).extension<TMusicTokens>();
    final globalAudio = ref.watch(localAudioProvider);
    final activeGlobal = globalAudio.hasValue ? globalAudio.value : null;
    final inlinePlayer =
        _player ??
        (s != null && activeGlobal?.mediaItem.value?.id == 'song:${s.id}'
            ? activeGlobal
            : null);
    final capsAsync = ref.watch(capabilitiesProvider);
    final mp3Supported =
        capsAsync.hasValue && capsAsync.value?['licensedAudioDownload'] == true;
    final mp3Available =
        s != null && (_hasLocalMp3 || (!s.isGuestLocal && s.audioReady));
    final mp3Downloading =
        (_activeJob?.isActive == true) ||
        (s != null &&
            !s.isGuestLocal &&
            (s.audioStatus == 'QUEUED' || s.audioStatus == 'RUNNING'));
    final ambient = albumAmbientColor(
      s?.thumbnail ?? s?.id,
      isDark: scheme.brightness == Brightness.dark,
    );
    final sourceUrl = s == null
        ? null
        : SharedLinks.supported(s.originalUrl)
        ? Uri.parse(s.originalUrl)
        : SharedLinks.supported(s.url)
        ? Uri.parse(s.url)
        : null;

    return Scaffold(
      appBar: AppBar(
        title: Text(context.tr('songs')),
        actions: [
          if (s != null)
            IconButton(
              tooltip: context.tr('editSong'),
              onPressed: () async {
                await context.push('/songs/${s.id}/edit');
                await _load();
              },
              icon: const Icon(Icons.edit_outlined),
            ),
        ],
      ),
      bottomNavigationBar: inlinePlayer == null
          ? const SafeArea(top: false, child: MiniPlayerDock())
          : null,
      body: s == null
          ? (_error != null
                ? LoadFailure(error: _error!, retry: _load)
                : const Center(child: CircularProgressIndicator()))
          : PageList(
              children: [
                if (_busy) const LinearProgressIndicator(),
                if (_error != null)
                  Notice(errorText(context, _error!), error: true),
                DecoratedBox(
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topCenter,
                      end: Alignment.bottomCenter,
                      colors: [ambient, scheme.surfaceContainerLowest],
                    ),
                    borderRadius: BorderRadius.circular(16),
                  ),
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(12, 20, 12, 14),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Center(
                          child: DecoratedBox(
                            decoration: BoxDecoration(
                              borderRadius: BorderRadius.circular(22),
                              boxShadow: tokens?.heroShadow,
                            ),
                            child: Cover(
                              url: s.thumbnail,
                              size: 196,
                              radius: 22,
                            ),
                          ),
                        ),
                        const SizedBox(height: 20),
                        Wrap(
                          alignment: WrapAlignment.center,
                          spacing: 8,
                          runSpacing: 6,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            if (mp3Available)
                              Container(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 10,
                                  vertical: 4,
                                ),
                                decoration: BoxDecoration(
                                  color: (tokens?.success ?? scheme.primary)
                                      .withValues(alpha: 0.14),
                                  borderRadius: BorderRadius.circular(999),
                                ),
                                child: Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    Icon(
                                      Icons.offline_pin_rounded,
                                      size: 14,
                                      color: tokens?.success ?? scheme.primary,
                                    ),
                                    const SizedBox(width: 4),
                                    Text(
                                      context.tr('mp3ReadyBadge'),
                                      style: Theme.of(context)
                                          .textTheme
                                          .bodySmall
                                          ?.copyWith(
                                            color:
                                                tokens?.success ??
                                                scheme.primary,
                                            fontWeight: FontWeight.w700,
                                            fontSize: 11.5,
                                          ),
                                    ),
                                  ],
                                ),
                              ),
                            if (formatDuration(s.durationSeconds) case final d?)
                              Text(
                                d,
                                style: Theme.of(context).textTheme.bodySmall,
                              ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        Text(
                          s.title,
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.headlineLarge
                              ?.copyWith(fontSize: 23),
                        ),
                        if (s.artist?.isNotEmpty == true) ...[
                          const SizedBox(height: 6),
                          Text(
                            s.artist!,
                            textAlign: TextAlign.center,
                            style: Theme.of(context).textTheme.titleMedium
                                ?.copyWith(color: scheme.onSurfaceVariant),
                          ),
                        ],
                        const SizedBox(height: 14),
                      ],
                    ),
                  ),
                ),
                if (s.capability == 'UNAVAILABLE')
                  Notice(context.tr('sourceUnavailable'), error: true),
                if (mp3Downloading) ...[
                  Text(
                    '${context.tr('downloadingMp3')}${_activeJob != null ? " (${_activeJob!.progressPercent}%)" : ""}',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                  const SizedBox(height: 8),
                  SmoothProgressBar(percent: _activeJob?.progressPercent),
                  const SizedBox(height: 14),
                ] else if (_activeJob?.status == 'FAILED' ||
                    s.audioStatus == 'FAILED') ...[
                  Notice(context.tr('mp3Failed'), error: true),
                ],
                if (inlinePlayer != null) ...[
                  LocalPlayerControls(inlinePlayer),
                  const SizedBox(height: 14),
                ],
                if (mp3Available) ...[
                  PressableScale(
                    enabled: !_busy,
                    child: FilledButton.icon(
                      key: const Key('play-mp3-button'),
                      onPressed: _busy ? null : () => _playMp3(s),
                      icon: const Icon(Icons.play_arrow_rounded, size: 24),
                      label: Text(context.tr('playMp3')),
                    ),
                  ),
                  const SizedBox(height: 10),
                ] else if (mp3Supported && !mp3Downloading) ...[
                  PressableScale(
                    enabled: !_busy,
                    child: FilledButton.icon(
                      key: const Key('download-mp3-button'),
                      onPressed: _busy ? null : () => _startMp3Download(s),
                      icon: const Icon(Icons.download_rounded),
                      label: Text(context.tr('downloadMp3Button')),
                    ),
                  ),
                  const SizedBox(height: 10),
                ],
                Card(
                  margin: EdgeInsets.zero,
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          context.tr('original'),
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 8),
                        ProviderBadge(
                          provider: s.provider,
                          creator: s.originalCreator,
                        ),
                        if (s.originalTitle?.trim().isNotEmpty == true) ...[
                          const SizedBox(height: 8),
                          Text(s.originalTitle!),
                        ],
                        if (sourceUrl != null) ...[
                          const SizedBox(height: 4),
                          Text(
                            sourceUrl.toString(),
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                          const SizedBox(height: 8),
                          FilledButton.tonalIcon(
                            key: const Key('open-source-button'),
                            onPressed: _busy
                                ? null
                                : () => _openSource(sourceUrl),
                            icon: const Icon(
                              Icons.open_in_new_rounded,
                              size: 18,
                            ),
                            label: Text(
                              context.tr(switch (s.provider) {
                                'youtube' => 'openYouTube',
                                'tiktok' => 'openTikTok',
                                'soundcloud' => 'openSoundCloud',
                                _ => 'openSource',
                              }),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 14),
                Wrap(
                  alignment: WrapAlignment.center,
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    OutlinedButton.icon(
                      onPressed: _busy
                          ? null
                          : () => _toggleFavoriteOptimistic(s),
                      icon: Icon(
                        s.favorite
                            ? Icons.favorite_rounded
                            : Icons.favorite_border_rounded,
                        color: s.favorite ? const Color(0xFFE05263) : null,
                      ),
                      label: Text(
                        context.tr(
                          s.favorite ? 'favoriteRemove' : 'favoriteAdd',
                        ),
                      ),
                    ),
                    OutlinedButton.icon(
                      onPressed: _busy
                          ? null
                          : () async {
                              final album = await showTMusicSheet<Album>(
                                context,
                                builder: (_) => const AlbumPicker(),
                              );
                              if (album != null) {
                                await _act(
                                  () => ref
                                      .read(libraryRepositoryProvider)
                                      .addToAlbum(album, s),
                                );
                              }
                            },
                      icon: const Icon(Icons.playlist_add_rounded),
                      label: Text(context.tr('addToAlbum')),
                    ),
                    OutlinedButton.icon(
                      onPressed: _busy
                          ? null
                          : () async {
                              await context.push('/songs/${s.id}/edit');
                              await _load();
                            },
                      icon: const Icon(Icons.edit_outlined, size: 18),
                      label: Text(context.tr('editSong')),
                    ),
                  ],
                ),
                const SizedBox(height: 22),
                // Studio Metadata & Album Membership Card
                Material(
                  color: scheme.surface,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(18),
                    side: BorderSide(
                      color: scheme.outlineVariant.withValues(alpha: 0.6),
                    ),
                  ),
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          context.tr('inAlbums'),
                          style: Theme.of(context).textTheme.titleMedium
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 8),
                        if (s.albumIds.isEmpty)
                          Text(
                            context.tr('notInAnyAlbum'),
                            style: Theme.of(context).textTheme.bodySmall,
                          )
                        else if (_memberAlbums.isNotEmpty)
                          Wrap(
                            spacing: 8,
                            runSpacing: 8,
                            children: _memberAlbums
                                .map(
                                  (a) => ActionChip(
                                    avatar: Cover(
                                      url: a.coverImageUrl,
                                      size: 20,
                                      radius: 10,
                                    ),
                                    label: Text(a.name),
                                    onPressed: () =>
                                        context.push('/albums/${a.id}'),
                                  ),
                                )
                                .toList(),
                          )
                        else
                          Text(
                            '${s.albumIds.length} ${context.tr('albums').toLowerCase()}',
                          ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                Center(
                  child: TextButton.icon(
                    style: TextButton.styleFrom(foregroundColor: scheme.error),
                    onPressed: _busy
                        ? null
                        : () async {
                            if (!await confirmAction(
                              context,
                              context.tr('songDeleteTitle'),
                              context.tr('songDeleteBody'),
                            )) {
                              return;
                            }
                            try {
                              await ref
                                  .read(libraryRepositoryProvider)
                                  .deleteSong(s);
                              try {
                                await (await ref.read(
                                  localLibraryProvider.future,
                                )).removeSongTrack(s.id);
                              } catch (_) {}
                              ref
                                  .read(libraryRevisionProvider.notifier)
                                  .changed();
                              if (context.mounted) context.pop();
                            } catch (e) {
                              if (mounted) setState(() => _error = e);
                            }
                          },
                    icon: const Icon(Icons.delete_outline_rounded),
                    label: Text(context.tr('delete')),
                  ),
                ),
              ],
            ),
    );
  }
}

class EditSongScreen extends ConsumerStatefulWidget {
  const EditSongScreen({super.key, required this.id});
  final String id;

  @override
  ConsumerState<EditSongScreen> createState() => _EditSongScreenState();
}

class _EditSongScreenState extends ConsumerState<EditSongScreen> {
  final _form = GlobalKey<FormState>();
  final _title = TextEditingController(), _artist = TextEditingController();
  Song? _song;
  List<Album> _albums = [];
  Set<String> _selectedAlbums = {};
  bool _busy = true;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _title.dispose();
    _artist.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _busy = true);
    try {
      final repo = ref.read(libraryRepositoryProvider);
      final s = await repo.song(widget.id);
      final albums = await repo.albums();
      if (mounted) {
        setState(() {
          _song = s;
          _title.text = s.title;
          _artist.text = s.artist ?? '';
          _albums = albums.items;
          _selectedAlbums = s.albumIds.toSet();
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _save() async {
    final s = _song;
    if (s == null || !_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      final updated = await repo.editSong(
        s,
        _title.text.trim(),
        _artist.text.trim(),
      );
      final currentSet = s.albumIds.toSet();
      for (final a in _albums) {
        final wasMember = currentSet.contains(a.id);
        final wantMember = _selectedAlbums.contains(a.id);
        if (!wasMember && wantMember) {
          final freshAlbum = await repo.album(a.id);
          await repo.addToAlbum(freshAlbum, updated);
        } else if (wasMember && !wantMember) {
          final freshAlbum = await repo.album(a.id);
          await repo.removeFromAlbum(freshAlbum, updated);
        }
      }
      ref.read(libraryRevisionProvider.notifier).changed();
      if (mounted) context.pop(updated);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(context.tr('editSong'))),
    body: _song == null && _error != null
        ? LoadFailure(error: _error!, retry: _load)
        : PageList(
            children: [
              if (_busy) const LinearProgressIndicator(),
              if (_error != null)
                Notice(errorText(context, _error!), error: true),
              if (_song != null)
                Form(
                  key: _form,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      TextFormField(
                        key: const Key('edit-song-title'),
                        controller: _title,
                        maxLength: 200,
                        decoration: InputDecoration(
                          labelText: context.tr('customTitle'),
                        ),
                        validator: (v) => requiredText(context, v),
                      ),
                      const SizedBox(height: 16),
                      TextFormField(
                        key: const Key('edit-song-artist'),
                        controller: _artist,
                        maxLength: 200,
                        decoration: InputDecoration(
                          labelText: context.tr('customArtist'),
                        ),
                      ),
                      if (_albums.isNotEmpty) ...[
                        const SizedBox(height: 20),
                        Text(
                          context.tr('inAlbums'),
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 8),
                        ..._albums.map(
                          (a) => CheckboxListTile(
                            contentPadding: EdgeInsets.zero,
                            secondary: Cover(
                              url: a.coverImageUrl,
                              size: 40,
                              radius: 8,
                            ),
                            title: Text(
                              a.name,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                            ),
                            value: _selectedAlbums.contains(a.id),
                            onChanged: (v) => setState(() {
                              if (v == true) {
                                _selectedAlbums.add(a.id);
                              } else {
                                _selectedAlbums.remove(a.id);
                              }
                            }),
                          ),
                        ),
                      ],
                      const SizedBox(height: 24),
                      FilledButton(
                        key: const Key('edit-song-save'),
                        onPressed: _busy ? null : _save,
                        child: Text(context.tr('save')),
                      ),
                    ],
                  ),
                ),
            ],
          ),
  );
}

class AlbumPicker extends ConsumerStatefulWidget {
  const AlbumPicker({super.key});
  @override
  ConsumerState<AlbumPicker> createState() => _AlbumPickerState();
}

class _AlbumPickerState extends ConsumerState<AlbumPicker> {
  List<Album> _items = [];
  Object? _error;
  bool _busy = true, _more = false;
  int _page = 1;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _busy = true);
    try {
      final p = await ref.read(libraryRepositoryProvider).albums(page: _page);
      if (mounted) {
        setState(() {
          _items = [..._items, ...p.items];
          _more = p.hasMore;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SizedBox(
      height: MediaQuery.sizeOf(context).height * .72,
      child: PageList(
        children: [
          Text(
            context.tr('chooseAlbums'),
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          if (_busy) const LinearProgressIndicator(),
          if (_error != null) ...[
            Notice(errorText(context, _error!), error: true),
            TextButton(onPressed: _load, child: Text(context.tr('retry'))),
          ],
          if (!_busy && _items.isEmpty) Text(context.tr('noAlbum')),
          ..._items.map(
            (a) => ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Cover(url: a.coverImageUrl, size: 44, radius: 8),
              title: Text(a.name),
              onTap: () => Navigator.pop(context, a),
            ),
          ),
          if (_more)
            OutlinedButton(
              onPressed: _busy
                  ? null
                  : () {
                      _page++;
                      _load();
                    },
              child: Text(context.tr('more')),
            ),
        ],
      ),
    ),
  );
}
