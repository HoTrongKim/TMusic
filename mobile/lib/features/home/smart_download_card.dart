import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/auth/external_auth.dart';
import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/storage/library_cache.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../shared/models/library_models.dart';
import '../library/library_repository.dart';
import '../playback/downloads_screen.dart';
import '../playback/local_audio.dart';
import '../share/shared_links.dart';

/// Spotify-Inspired Link-to-MP3 Card supporting YouTube, TikTok, and SoundCloud
/// with explicit original video/audio source attribution (and no clipboard-paste button).
class SmartDownloadCard extends ConsumerStatefulWidget {
  const SmartDownloadCard({super.key, this.initialAlbumId});
  final String? initialAlbumId;

  @override
  ConsumerState<SmartDownloadCard> createState() => _SmartDownloadCardState();
}

class _SmartDownloadCardState extends ConsumerState<SmartDownloadCard> {
  final _form = GlobalKey<FormState>();
  final _url = TextEditingController();
  final _title = TextEditingController();
  final _artist = TextEditingController();

  List<Album> _albums = [];
  String? _selectedAlbumId;
  String? _lastUsedAlbumId;

  LinkPreview? _preview;
  String? _lastResolvedUrl;
  bool _busy = false;
  bool _showRenameFields = false;
  Object? _error;
  String? _localUrlError;

  Song? _savedSong;
  String? _savedAlbumId;
  bool _offlineSaved = false;

  Timer? _inputDebounce;
  Timer? _directJobPoll;
  int _resolveToken = 0;
  String _prevUrlText = '';

  @override
  void initState() {
    super.initState();
    final draft = ref.read(linkDraftProvider);
    _url.text = draft.url;
    _prevUrlText = draft.url;
    _title.text = draft.title;
    _artist.text = draft.artist;
    _selectedAlbumId = widget.initialAlbumId ?? draft.albumId;
    _loadAlbumsAndDefaults();
    if (_url.text.trim().isNotEmpty &&
        SharedLinks.supported(_url.text.trim())) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _detect());
    }
  }

  @override
  void didUpdateWidget(covariant SmartDownloadCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.initialAlbumId != null &&
        widget.initialAlbumId != oldWidget.initialAlbumId) {
      setState(() => _selectedAlbumId = widget.initialAlbumId);
    }
  }

  @override
  void dispose() {
    _inputDebounce?.cancel();
    _directJobPoll?.cancel();
    _url.dispose();
    _title.dispose();
    _artist.dispose();
    super.dispose();
  }

  Future<void> _loadAlbumsAndDefaults() async {
    try {
      final cache = ref.read(libraryCacheProvider);
      final remembered = await cache.readLastUsedAlbumId();
      final page = await ref.read(libraryRepositoryProvider).albums();
      if (!mounted) return;
      setState(() {
        _albums = page.items;
        _lastUsedAlbumId = remembered;
        if (_selectedAlbumId == null &&
            widget.initialAlbumId == null &&
            remembered != null &&
            page.items.any((a) => a.id == remembered)) {
          _selectedAlbumId = remembered;
        }
      });
    } catch (_) {}
  }

  void _onUrlChanged(String value) {
    _inputDebounce?.cancel();
    final delta = (value.length - _prevUrlText.length).abs();
    _prevUrlText = value;
    ref.read(linkDraftProvider.notifier).update(url: value);

    final trimmed = value.trim();
    if (trimmed != _lastResolvedUrl) {
      setState(() {
        _preview = null;
        _savedSong = null;
        _savedAlbumId = null;
        _offlineSaved = false;
        _localUrlError = null;
        _error = null;
      });
    }

    if (delta >= 12 && SharedLinks.supported(trimmed)) {
      _inputDebounce = Timer(const Duration(milliseconds: 260), () {
        if (mounted && _url.text.trim() == trimmed) {
          _detect();
        }
      });
    }
  }

  void _clearUrl() {
    _inputDebounce?.cancel();
    _directJobPoll?.cancel();
    _resolveToken++;
    _url.clear();
    _title.clear();
    _artist.clear();
    _prevUrlText = '';
    ref.read(linkDraftProvider.notifier).clear();
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      _preview = null;
      _lastResolvedUrl = null;
      _savedSong = null;
      _savedAlbumId = null;
      _offlineSaved = false;
      _showRenameFields = false;
      _error = null;
      _localUrlError = null;
    });
  }

  Future<void> _detect() async {
    final trimmed = _url.text.trim();
    if (trimmed.isEmpty) return;
    FocusManager.instance.primaryFocus?.unfocus();

    if (!SharedLinks.supported(trimmed)) {
      setState(() {
        _preview = null;
        _localUrlError = context.tr('invalidSupportedUrl');
        _error = null;
      });
      return;
    }

    final token = ++_resolveToken;
    setState(() {
      _busy = true;
      _error = null;
      _localUrlError = null;
      _savedSong = null;
      _savedAlbumId = null;
      _offlineSaved = false;
    });

    try {
      final p = await ref.read(libraryRepositoryProvider).resolve(trimmed);
      if (!mounted || token != _resolveToken) return;
      TMusicHaptics.light();
      setState(() {
        _preview = p;
        _lastResolvedUrl = trimmed;
        final existing = p.existingSong;
        if (existing != null) {
          _title.text = existing.title;
          _artist.text = existing.artist ?? '';
          if (existing.albumIds.isNotEmpty && _selectedAlbumId == null) {
            _selectedAlbumId = existing.albumIds.first;
          }
          _showRenameFields = false;
        } else {
          _title.text = p.title ?? '';
          _artist.text = p.creator ?? '';
          _showRenameFields = (p.title ?? '').trim().isEmpty;
        }
      });
    } catch (e) {
      if (mounted && token == _resolveToken) {
        setState(() => _error = e);
      }
    } finally {
      if (mounted && token == _resolveToken) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _saveAndDownload({required bool mp3Supported}) async {
    final ownerId = ref.read(sessionProvider).value?.userId;
    final p = _preview;
    if (p == null) return;
    if (_title.text.trim().isEmpty) {
      setState(() => _showRenameFields = true);
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _form.currentState?.validate();
      });
      return;
    }
    if (_showRenameFields && !(_form.currentState?.validate() ?? true)) return;

    FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      _busy = true;
      _error = null;
      _offlineSaved = false;
    });

    try {
      final targetAlbums = _selectedAlbumId != null
          ? <String>[_selectedAlbumId!]
          : const <String>[];
      final repo = ref.read(libraryRepositoryProvider);
      final saved = await repo.saveLink(
        _url.text.trim(),
        _title.text.trim(),
        _artist.text.trim(),
        targetAlbums,
        downloadMp3: mp3Supported,
        rightsConfirmed: mp3Supported,
      );

      if (_selectedAlbumId != null) {
        _lastUsedAlbumId = _selectedAlbumId;
        unawaited(
          ref.read(libraryCacheProvider).writeLastUsedAlbumId(_selectedAlbumId),
        );
      }

      ref.read(libraryRevisionProvider.notifier).changed();
      ref.read(sharedLinksProvider.notifier).consume(_url.text.trim());
      ref.read(linkDraftProvider.notifier).clear();
      TMusicHaptics.medium();

      if (!mounted) return;
      setState(() {
        _savedSong = saved;
        _savedAlbumId = _selectedAlbumId;
      });

      if (saved.downloadJobId != null) {
        unawaited(
          ref.read(downloadJobsProvider.notifier).refresh(silent: true),
        );
        _watchJobUntilDone(saved, saved.downloadJobId!, ownerId);
      } else if (mp3Supported && saved.audioReady) {
        await _cacheOffline(saved);
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _watchJobUntilDone(Song song, String jobId, String? ownerId) {
    _directJobPoll?.cancel();
    Future<void> poll() async {
      try {
        if (ref.read(sessionProvider).value?.userId != ownerId) return;
        final job = await ref
            .read(libraryRepositoryProvider)
            .downloadJob(jobId);
        if (!mounted || ref.read(sessionProvider).value?.userId != ownerId) {
          return;
        }
        ref.read(downloadJobsProvider.notifier).trackJob(job, ownerId: ownerId);
        if (job.isActive) {
          _directJobPoll = Timer(const Duration(milliseconds: 1400), poll);
        } else if (job.audioReady) {
          await _cacheOffline(song);
          ref.read(libraryRevisionProvider.notifier).changed();
        }
      } catch (_) {}
    }

    poll();
  }

  Future<void> _cacheOffline(Song song) async {
    try {
      final local = await ref.read(localLibraryProvider.future);
      if (!await local.hasDownloadedSong(song.id) && !song.isGuestLocal) {
        final bytes = await ref
            .read(libraryRepositoryProvider)
            .fetchSongAudioBytes(song.id);
        await local.saveDownloadedSongMp3(song, bytes);
      }
      if (mounted) setState(() => _offlineSaved = true);
    } catch (_) {}
  }

  Future<void> _openAlbumSelectorSheet() async {
    FocusManager.instance.primaryFocus?.unfocus();
    final choice = await showTMusicSheet<String>(
      context,
      builder: (sheetCtx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 4, 20, 20),
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  sheetCtx.tr('chooseAlbums'),
                  style: Theme.of(sheetCtx).textTheme.headlineSmall,
                ),
                const SizedBox(height: 12),
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const TintedIconBadge(
                    icon: Icons.inbox_rounded,
                    size: 44,
                  ),
                  title: Text(sheetCtx.tr('unclassified')),
                  trailing: _selectedAlbumId == null
                      ? const Icon(
                          Icons.check_circle_rounded,
                          color: TMusicTheme.spotifyGreen,
                        )
                      : null,
                  onTap: () => Navigator.pop(sheetCtx, '__UNCLASSIFIED__'),
                ),
                ..._albums.map(
                  (a) => ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: Cover(url: a.coverImageUrl, size: 44, radius: 4),
                    title: Text(
                      a.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    trailing: _selectedAlbumId == a.id
                        ? const Icon(
                            Icons.check_circle_rounded,
                            color: TMusicTheme.spotifyGreen,
                          )
                        : null,
                    onTap: () => Navigator.pop(sheetCtx, a.id),
                  ),
                ),
                const SizedBox(height: 8),
                FilledButton.icon(
                  onPressed: () => Navigator.pop(sheetCtx, '__CREATE_NEW__'),
                  icon: const Icon(Icons.add_rounded),
                  label: Text(sheetCtx.tr('createAlbum')),
                ),
              ],
            ),
          ),
        ),
      ),
    );

    if (!mounted || choice == null) return;
    if (choice == '__UNCLASSIFIED__') {
      TMusicHaptics.selection();
      setState(() => _selectedAlbumId = null);
    } else if (choice == '__CREATE_NEW__') {
      final created = await context.push<Album>('/albums/new');
      if (created != null && mounted) {
        await _loadAlbumsAndDefaults();
        setState(() {
          _selectedAlbumId = created.id;
          _lastUsedAlbumId = created.id;
        });
      }
    } else {
      TMusicHaptics.selection();
      setState(() => _selectedAlbumId = choice);
    }
  }

  @override
  Widget build(BuildContext context) {
    final shared = ref.watch(sharedLinksProvider);
    final capsAsync = ref.watch(capabilitiesProvider);
    final mp3Supported =
        capsAsync.hasValue && capsAsync.value?['licensedAudioDownload'] == true;
    final scheme = Theme.of(context).colorScheme;

    ref.listen(libraryRevisionProvider, (_, _) => _loadAlbumsAndDefaults());
    ref.listen(linkDraftProvider, (_, next) {
      if (next.albumId != null && next.albumId != _selectedAlbumId) {
        setState(() => _selectedAlbumId = next.albumId);
      }
      if (next.url.isNotEmpty && next.url != _url.text) {
        _url.text = next.url;
        _prevUrlText = next.url;
        if (SharedLinks.supported(next.url.trim())) {
          _detect();
        }
      }
    });

    final watchedJob = _savedSong != null
        ? ref.watch(songDownloadJobProvider(_savedSong!.id))
        : null;
    final Album? activeAlbum = _selectedAlbumId == null
        ? null
        : _albums.where((a) => a.id == _selectedAlbumId).firstOrNull;
    final Album? lastAlbum = _lastUsedAlbumId == null
        ? (_albums.isNotEmpty ? _albums.first : null)
        : (_albums.where((a) => a.id == _lastUsedAlbumId).firstOrNull ??
              (_albums.isNotEmpty ? _albums.first : null));

    return Material(
      key: const Key('home-add'),
      color: scheme.surface,
      borderRadius: BorderRadius.circular(12),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: AnimatedSize(
          duration: context.reduceMotion
              ? Duration.zero
              : const Duration(milliseconds: 220),
          curve: Curves.easeOutCubic,
          alignment: Alignment.topCenter,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Wrap(
                alignment: WrapAlignment.spaceBetween,
                crossAxisAlignment: WrapCrossAlignment.center,
                spacing: 8,
                runSpacing: 6,
                children: [
                  Text(
                    context.tr('downloadMp3Primary'),
                    style: Theme.of(context).textTheme.titleMedium
                        ?.copyWith(fontWeight: FontWeight.w800),
                  ),
                  const Wrap(
                    spacing: 6,
                    runSpacing: 4,
                    children: [
                      ProviderBadge(
                        provider: 'youtube',
                        showSourcePrefix: false,
                      ),
                      ProviderBadge(
                        provider: 'tiktok',
                        showSourcePrefix: false,
                      ),
                      ProviderBadge(
                        provider: 'soundcloud',
                        showSourcePrefix: false,
                      ),
                    ],
                  ),
                ],
              ),
              const SizedBox(height: 10),
              if (shared.isNotEmpty) ...[
                Text(
                  context.tr('shared'),
                  style: Theme.of(context).textTheme.labelLarge,
                ),
                const SizedBox(height: 6),
                ...shared.map(
                  (u) => Padding(
                    padding: const EdgeInsets.only(bottom: 6),
                    child: ActionChip(
                      avatar: const Icon(Icons.share_rounded, size: 16),
                      label: Text(
                        u,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      onPressed: () {
                        _url.text = u;
                        _prevUrlText = u;
                        _detect();
                      },
                    ),
                  ),
                ),
                const SizedBox(height: 6),
              ],
              TextField(
                key: const Key('link-url'),
                controller: _url,
                keyboardType: TextInputType.url,
                textInputAction: TextInputAction.done,
                autocorrect: false,
                scrollPadding: const EdgeInsets.fromLTRB(20, 20, 20, 140),
                onChanged: _onUrlChanged,
                onSubmitted: (_) => _detect(),
                decoration: InputDecoration(
                  hintText: 'YouTube, TikTok, SoundCloud URL…',
                  prefixIcon: const Icon(Icons.search_rounded),
                  suffixIcon: _url.text.isEmpty
                      ? null
                      : IconButton(
                          key: const Key('link-clear'),
                          tooltip: context.tr('clearUrl'),
                          onPressed: _busy ? null : _clearUrl,
                          icon: const Icon(Icons.close_rounded),
                        ),
                ),
              ),
              const SizedBox(height: 10),
              FilledButton.icon(
                key: const Key('link-detect'),
                onPressed: _busy ? null : _detect,
                icon: const Icon(Icons.graphic_eq_rounded, size: 19),
                label: Text(context.tr('detect')),
              ),
              if (_busy) ...[
                const SizedBox(height: 12),
                const SmoothProgressBar(),
              ],
              if (_localUrlError != null) ...[
                const SizedBox(height: 8),
                Notice(_localUrlError!, error: true),
              ],
              if (_error != null) ...[
                const SizedBox(height: 8),
                Notice(errorText(context, _error!), error: true),
              ],
              if (_savedSong case final saved?) ...[
                const SizedBox(height: 12),
                if (watchedJob != null && watchedJob.isActive) ...[
                  Text(
                    '${context.tr('downloadingMp3')} (${watchedJob.progressPercent}%)',
                    style: Theme.of(context).textTheme.labelLarge,
                  ),
                  const SizedBox(height: 8),
                  SmoothProgressBar(percent: watchedJob.progressPercent),
                  const SizedBox(height: 12),
                ] else if (watchedJob?.status == 'FAILED') ...[
                  Notice(context.tr('mp3Failed'), error: true),
                  const SizedBox(height: 8),
                ] else ...[
                  Notice(
                    _offlineSaved ||
                            saved.audioReady ||
                            watchedJob?.audioReady == true
                        ? context.tr('downloadCompleteTitle')
                        : context.tr('saved'),
                  ),
                  const SizedBox(height: 10),
                ],
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    if (_offlineSaved ||
                        saved.audioReady ||
                        watchedJob?.audioReady == true)
                      PressableScale(
                        child: FilledButton.icon(
                          key: const Key('home-play-downloaded-mp3'),
                          onPressed: () async {
                            try {
                              final local = await ref.read(
                                localLibraryProvider.future,
                              );
                              final track = saved.isGuestLocal
                                  ? await local.getDownloadedSongTrack(saved)
                                  : await local.ensureSongTrack(
                                      saved,
                                      () => ref
                                          .read(libraryRepositoryProvider)
                                          .fetchSongAudioBytes(saved.id),
                                    );
                              if (track == null) return;
                              final player = await ref.read(
                                localAudioProvider.future,
                              );
                              TMusicHaptics.light();
                              await player.start([track], 0);
                            } catch (e) {
                              if (mounted) setState(() => _error = e);
                            }
                          },
                          icon: const Icon(Icons.play_arrow_rounded),
                          label: Text(context.tr('playMp3')),
                        ),
                      ),
                    if (_savedAlbumId != null)
                      OutlinedButton.icon(
                        key: const Key('open-saved-album'),
                        onPressed: () => context.push('/albums/$_savedAlbumId'),
                        icon: const Icon(Icons.album_rounded, size: 18),
                        label: Text(context.tr('viewInAlbum')),
                      )
                    else
                      OutlinedButton.icon(
                        onPressed: () => context.push('/songs/${saved.id}'),
                        icon: const Icon(Icons.music_note_rounded, size: 18),
                        label: Text(context.tr('openSavedSong')),
                      ),
                    OutlinedButton.icon(
                      onPressed: () => context.push('/local'),
                      icon: const Icon(Icons.folder_outlined, size: 18),
                      label: Text(context.tr('viewSavedFiles')),
                    ),
                    TextButton.icon(
                      onPressed: _clearUrl,
                      icon: const Icon(Icons.add_rounded),
                      label: Text(context.tr('addAnotherLink')),
                    ),
                  ],
                ),
              ] else if (_preview case final preview?) ...[
                const SizedBox(height: 12),
                SongMetadataPreviewCard(preview: preview),
                const SizedBox(height: 6),
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton.icon(
                    onPressed: () =>
                        setState(() => _showRenameFields = !_showRenameFields),
                    icon: Icon(
                      _showRenameFields
                          ? Icons.expand_less_rounded
                          : Icons.edit_outlined,
                      size: 18,
                    ),
                    label: Text(context.tr('renameToggle')),
                  ),
                ),
                if (_showRenameFields || (preview.title ?? '').trim().isEmpty)
                  Form(
                    key: _form,
                    child: Padding(
                      padding: const EdgeInsets.only(top: 6, bottom: 8),
                      child: Column(
                        children: [
                          TextFormField(
                            key: const Key('link-title'),
                            controller: _title,
                            maxLength: 200,
                            readOnly: preview.existingSong != null,
                            scrollPadding: const EdgeInsets.fromLTRB(
                              20,
                              20,
                              20,
                              140,
                            ),
                            decoration: InputDecoration(
                              labelText: context.tr('customTitle'),
                            ),
                            validator: (v) => requiredText(context, v),
                          ),
                          const SizedBox(height: 10),
                          TextFormField(
                            controller: _artist,
                            maxLength: 200,
                            readOnly: preview.existingSong != null,
                            decoration: InputDecoration(
                              labelText: context.tr('customArtist'),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                const SizedBox(height: 6),
                Text(
                  context.tr('saveToLabel'),
                  style: Theme.of(context).textTheme.labelLarge,
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    if (lastAlbum != null)
                      ChoiceChip(
                        showCheckmark: false,
                        avatar: Cover(
                          url: lastAlbum.coverImageUrl,
                          size: 18,
                          radius: 3,
                        ),
                        label: Text(
                          activeAlbum != null && activeAlbum.id != lastAlbum.id
                              ? activeAlbum.name
                              : '${context.tr('lastUsedPrefix')} ${lastAlbum.name}',
                          overflow: TextOverflow.ellipsis,
                        ),
                        selected: _selectedAlbumId != null,
                        onSelected: (_) {
                          TMusicHaptics.selection();
                          setState(
                            () => _selectedAlbumId =
                                (activeAlbum ?? lastAlbum).id,
                          );
                        },
                      ),
                    ChoiceChip(
                      key: const Key('select-unclassified'),
                      showCheckmark: false,
                      label: Text(context.tr('unclassified')),
                      selected: _selectedAlbumId == null,
                      onSelected: (_) {
                        TMusicHaptics.selection();
                        setState(() => _selectedAlbumId = null);
                      },
                    ),
                    ActionChip(
                      key: const Key('select-other-album'),
                      avatar: Icon(
                        _albums.isEmpty
                            ? Icons.add_rounded
                            : Icons.folder_open_rounded,
                        size: 17,
                      ),
                      label: Text(
                        context.tr(
                          _albums.isEmpty ? 'createAlbum' : 'otherAlbum',
                        ),
                      ),
                      onPressed: _busy ? null : _openAlbumSelectorSheet,
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                PressableScale(
                  enabled: !_busy && preview.capability != 'UNAVAILABLE',
                  child: FilledButton.icon(
                    key: const Key('link-save'),
                    onPressed: _busy || preview.capability == 'UNAVAILABLE'
                        ? null
                        : () => _saveAndDownload(mp3Supported: mp3Supported),
                    icon: Icon(
                      mp3Supported
                          ? Icons.download_rounded
                          : Icons.bookmark_add_outlined,
                    ),
                    label: Text(
                      context.tr(
                        mp3Supported ? 'downloadMp3Primary' : 'saveLink',
                      ),
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class SongMetadataPreviewCard extends StatelessWidget {
  const SongMetadataPreviewCard({super.key, required this.preview});
  final LinkPreview preview;

  @override
  Widget build(BuildContext context) {
    final duration = formatDuration(preview.durationSeconds);
    final scheme = Theme.of(context).colorScheme;
    return Container(
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(10),
      ),
      padding: const EdgeInsets.all(12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Cover(url: preview.thumbnail, size: 68, radius: 6),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: 8,
                      runSpacing: 4,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        ProviderBadge(
                          provider: preview.provider,
                          creator: preview.creator,
                        ),
                        if (duration != null)
                          Text(
                            duration,
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    if (preview.title != null)
                      Text(
                        preview.title!,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.titleMedium
                            ?.copyWith(fontWeight: FontWeight.w700),
                      )
                    else
                      Text(
                        context.tr('manualMetadata'),
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    const SizedBox(height: 3),
                    Text(
                      preview.canonicalUrl,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(fontSize: 11),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          CapabilityBadge(capability: preview.capability),
          if (preview.existingId != null) ...[
            const SizedBox(height: 8),
            Notice(context.tr('duplicate')),
            if (preview.existingSong case final existing?)
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: () => context.push('/songs/${existing.id}'),
                  icon: const Icon(Icons.open_in_new_rounded, size: 18),
                  label: Text(context.tr('viewLibrary')),
                ),
              ),
          ],
        ],
      ),
    );
  }
}
