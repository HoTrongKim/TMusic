import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/localization/app_copy.dart';
import '../../core/storage/app_metadata.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../shared/models/library_models.dart';
import '../library/library_repository.dart';
import '../playback/local_audio.dart';
import '../share/shared_links.dart';

/// Shared real-data song actions used by swipe menus, three-dot sheets,
/// multi-selection bars, and the Full Player.
abstract final class SongActions {
  static Future<Song?> toggleFavorite(
    BuildContext context,
    WidgetRef ref,
    Song song, {
    void Function(Song updated)? onOptimistic,
    void Function(Song original)? onRollback,
  }) async {
    final target = !song.favorite;
    TMusicHaptics.light();
    final optimistic = song.copyWith(favorite: target);
    onOptimistic?.call(optimistic);
    try {
      final saved = await ref.read(libraryRepositoryProvider).favorite(song);
      ref.read(libraryRevisionProvider.notifier).changed();
      return saved;
    } catch (e) {
      onRollback?.call(song);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(errorText(context, e))),
        );
      }
      return null;
    }
  }

  /// Removes [song] from [album] only (never deletes the MP3 or other albums)
  /// and offers an Undo SnackBar that restores the album membership.
  static Future<bool> removeFromAlbumWithUndo(
    BuildContext context,
    WidgetRef ref,
    Album album,
    Song song, {
    VoidCallback? onChanged,
  }) async {
    final repo = ref.read(libraryRepositoryProvider);
    final messenger = ScaffoldMessenger.of(context);
    final removedMsg = context.tr('removeFromAlbumSuccess');
    final undoLabel = context.tr('undo');
    try {
      final freshAlbum = await repo.album(album.id);
      await repo.removeFromAlbum(freshAlbum, song);
      ref.read(libraryRevisionProvider.notifier).changed();
      onChanged?.call();
      if (context.mounted) {
        messenger.hideCurrentSnackBar();
        messenger.showSnackBar(
          SnackBar(
            content: Text(removedMsg),
            action: SnackBarAction(
              label: undoLabel,
              textColor: TMusicTheme.spotifyGreen,
              onPressed: () async {
                try {
                  final latestAlbum = await repo.album(album.id);
                  await repo.addToAlbum(latestAlbum, song);
                  ref.read(libraryRevisionProvider.notifier).changed();
                  onChanged?.call();
                } catch (e) {
                  if (context.mounted) {
                    messenger.showSnackBar(
                      SnackBar(content: Text(errorText(context, e))),
                    );
                  }
                }
              },
            ),
          ),
        );
      }
      return true;
    } catch (e) {
      if (context.mounted) {
        messenger.showSnackBar(SnackBar(content: Text(errorText(context, e))));
      }
      return false;
    }
  }

  /// Confirms and deletes [song] from the library, explaining multi-album impact
  /// and safely removing it from active playback if currently playing.
  static Future<bool> confirmAndDeleteFromLibrary(
    BuildContext context,
    WidgetRef ref,
    Song song, {
    bool? hasLocalMp3,
    VoidCallback? onDeleted,
  }) async {
    var localPresent = hasLocalMp3 ?? song.audioReady;
    if (hasLocalMp3 == null) {
      try {
        final local = await ref.read(localLibraryProvider.future);
        localPresent = await local.hasDownloadedSong(song.id);
      } catch (_) {}
    }
    if (!context.mounted) return false;

    var deleteLocalFile = localPresent;
    final albumCount = song.albumIds.length;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogCtx) => StatefulBuilder(
        builder: (dialogCtx, setModal) => AlertDialog(
          title: Text(dialogCtx.tr('songDeleteTitle')),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(dialogCtx.tr('songDeleteBody')),
              if (albumCount > 1) ...[
                const SizedBox(height: 10),
                Text(
                  dialogCtx
                      .tr('deleteSongMultiAlbumWarning')
                      .replaceAll('{count}', '$albumCount'),
                  style: Theme.of(dialogCtx).textTheme.bodySmall?.copyWith(
                    color: Theme.of(dialogCtx).colorScheme.error,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
              if (localPresent) ...[
                const SizedBox(height: 10),
                CheckboxListTile(
                  key: const Key('delete-local-mp3-checkbox'),
                  contentPadding: EdgeInsets.zero,
                  value: deleteLocalFile,
                  onChanged: (v) => setModal(() => deleteLocalFile = v ?? true),
                  title: Text(
                    dialogCtx.tr('deleteLocalMp3Option'),
                    style: Theme.of(dialogCtx).textTheme.bodyMedium,
                  ),
                ),
              ],
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogCtx, false),
              child: Text(dialogCtx.tr('cancel')),
            ),
            FilledButton(
              style: FilledButton.styleFrom(
                backgroundColor: Theme.of(dialogCtx).colorScheme.error,
                foregroundColor: Theme.of(dialogCtx).colorScheme.onError,
              ),
              onPressed: () => Navigator.pop(dialogCtx, true),
              child: Text(dialogCtx.tr('delete')),
            ),
          ],
        ),
      ),
    );
    if (confirmed != true || !context.mounted) return false;

    final messenger = ScaffoldMessenger.of(context);
    final deletedNotice = context.tr('deletedFromLibrary');
    try {
      // Safely detach from active audio queue before removing files.
      final audioAsync = ref.read(localAudioProvider);
      if (audioAsync.hasValue && audioAsync.value != null) {
        await audioAsync.value!.removeSongFromQueue(song.id);
      }
      await ref.read(libraryRepositoryProvider).deleteSong(song);
      if (deleteLocalFile) {
        try {
          await (await ref.read(
            localLibraryProvider.future,
          )).removeSongTrack(song.id);
        } catch (_) {}
      }
      ref.read(libraryRevisionProvider.notifier).changed();
      onDeleted?.call();
      if (context.mounted) {
        messenger.showSnackBar(SnackBar(content: Text(deletedNotice)));
      }
      return true;
    } catch (e) {
      if (context.mounted) {
        messenger.showSnackBar(SnackBar(content: Text(errorText(context, e))));
      }
      return false;
    }
  }

  static Future<void> enqueueSong(
    BuildContext context,
    WidgetRef ref,
    Song song, {
    required bool playNext,
  }) async {
    final messenger = ScaffoldMessenger.of(context);
    try {
      final local = await ref.read(localLibraryProvider.future);
      final repo = ref.read(libraryRepositoryProvider);
      final track = song.isGuestLocal
          ? await local.getDownloadedSongTrack(song)
          : await local.ensureSongTrack(
              song,
              () => repo.fetchSongAudioBytes(song.id),
            );
      if (track == null) {
        if (context.mounted) {
          messenger.showSnackBar(
            SnackBar(content: Text(context.tr('emptyDownloaded'))),
          );
        }
        return;
      }
      final player = await ref.read(localAudioProvider.future);
      if (playNext) {
        await player.playNext(track);
      } else {
        await player.addToQueue(track);
      }
      TMusicHaptics.light();
      if (context.mounted) {
        messenger.showSnackBar(
          SnackBar(
            content: Text(
              context.tr(playNext ? 'addedToPlayNext' : 'addedToQueue'),
            ),
          ),
        );
      }
    } catch (e) {
      if (context.mounted) {
        messenger.showSnackBar(SnackBar(content: Text(errorText(context, e))));
      }
    }
  }

  static Future<bool?> openAlbumPicker(
    BuildContext context, {
    required List<Song> songs,
  }) => showTMusicSheet<bool>(
    context,
    builder: (_) => SongAlbumPickerSheet(songs: songs),
  );

  static Future<void> showMoreSheet(
    BuildContext context,
    WidgetRef ref,
    Song song, {
    Album? albumContext,
    bool? canPlay,
    VoidCallback? onChanged,
    void Function(Song updated)? onOptimisticFavorite,
    void Function(Song original)? onRollbackFavorite,
  }) async {
    final playable = canPlay ?? song.audioReady;
    final rawUrl = song.originalUrl.isNotEmpty ? song.originalUrl : song.url;
    final sourceUrl = SharedLinks.supported(rawUrl)
        ? Uri.tryParse(rawUrl.trim())
        : null;

    await showTMusicSheet<void>(
      context,
      builder: (sheetCtx) => SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Cover(url: song.thumbnail, size: 48, radius: 8),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          song.title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(sheetCtx).textTheme.titleMedium
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          song.sourceAttribution,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(sheetCtx).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const Divider(),
              if (playable) ...[
                ListTile(
                  key: const Key('song-more-play-next'),
                  leading: const Icon(Icons.playlist_play_rounded),
                  title: Text(sheetCtx.tr('playNext')),
                  onTap: () {
                    Navigator.pop(sheetCtx);
                    enqueueSong(context, ref, song, playNext: true);
                  },
                ),
                ListTile(
                  key: const Key('song-more-add-queue'),
                  leading: const Icon(Icons.queue_music_rounded),
                  title: Text(sheetCtx.tr('addToQueue')),
                  onTap: () {
                    Navigator.pop(sheetCtx);
                    enqueueSong(context, ref, song, playNext: false);
                  },
                ),
              ],
              ListTile(
                key: const Key('song-more-favorite'),
                leading: Icon(
                  song.favorite
                      ? Icons.favorite_rounded
                      : Icons.favorite_border_rounded,
                  color: song.favorite ? const Color(0xFFE05263) : null,
                ),
                title: Text(
                  sheetCtx.tr(song.favorite ? 'favoriteRemove' : 'favoriteAdd'),
                ),
                onTap: () {
                  Navigator.pop(sheetCtx);
                  toggleFavorite(
                    context,
                    ref,
                    song,
                    onOptimistic: onOptimisticFavorite,
                    onRollback: onRollbackFavorite,
                  );
                },
              ),
              ListTile(
                key: const Key('song-more-add-album'),
                leading: const Icon(Icons.playlist_add_rounded),
                title: Text(sheetCtx.tr('addToAlbum')),
                onTap: () async {
                  Navigator.pop(sheetCtx);
                  final updated = await openAlbumPicker(context, songs: [song]);
                  if (updated == true) onChanged?.call();
                },
              ),
              ListTile(
                key: const Key('song-more-view-info'),
                leading: const Icon(Icons.info_outline_rounded),
                title: Text(sheetCtx.tr('viewSongInfo')),
                onTap: () {
                  Navigator.pop(sheetCtx);
                  context.push('/songs/${song.id}');
                },
              ),
              ListTile(
                key: const Key('song-more-edit'),
                leading: const Icon(Icons.edit_outlined),
                title: Text(sheetCtx.tr('editSong')),
                onTap: () {
                  Navigator.pop(sheetCtx);
                  context.push('/songs/${song.id}/edit');
                },
              ),
              if (song.albumIds.isNotEmpty)
                ListTile(
                  key: const Key('song-more-view-albums'),
                  leading: const Icon(Icons.album_outlined),
                  title: Text(sheetCtx.tr('viewContainingAlbums')),
                  onTap: () {
                    Navigator.pop(sheetCtx);
                    _openContainingAlbumsSheet(context, ref, song);
                  },
                ),
              if (sourceUrl != null) ...[
                ListTile(
                  key: const Key('song-more-copy-link'),
                  leading: const Icon(Icons.share_outlined),
                  title: Text(sheetCtx.tr('shareSourceLink')),
                  onTap: () async {
                    Navigator.pop(sheetCtx);
                    await Clipboard.setData(
                      ClipboardData(text: sourceUrl.toString()),
                    );
                    TMusicHaptics.light();
                    if (context.mounted) {
                      ScaffoldMessenger.of(context).showSnackBar(
                        SnackBar(
                          content: Text(context.tr('sourceLinkCopied')),
                        ),
                      );
                    }
                  },
                ),
                ListTile(
                  key: const Key('song-more-open-source'),
                  leading: const Icon(Icons.open_in_new_rounded),
                  title: Text(
                    sheetCtx.tr(switch (song.provider) {
                      'youtube' => 'openYouTube',
                      'tiktok' => 'openTikTok',
                      'soundcloud' => 'openSoundCloud',
                      _ => 'openSource',
                    }),
                  ),
                  onTap: () async {
                    Navigator.pop(sheetCtx);
                    try {
                      await launchUrl(
                        sourceUrl,
                        mode: LaunchMode.externalApplication,
                      );
                    } catch (_) {}
                  },
                ),
              ],
              const Divider(),
              if (albumContext != null)
                ListTile(
                  key: const Key('song-more-remove-album'),
                  leading: Icon(
                    Icons.remove_circle_outline_rounded,
                    color: Theme.of(sheetCtx).colorScheme.error,
                  ),
                  title: Text(
                    sheetCtx.tr('swipeRemoveFromAlbum'),
                    style: TextStyle(
                      color: Theme.of(sheetCtx).colorScheme.error,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  onTap: () {
                    Navigator.pop(sheetCtx);
                    removeFromAlbumWithUndo(
                      context,
                      ref,
                      albumContext,
                      song,
                      onChanged: onChanged,
                    );
                  },
                )
              else
                ListTile(
                  key: const Key('song-more-delete-library'),
                  leading: Icon(
                    Icons.delete_outline_rounded,
                    color: Theme.of(sheetCtx).colorScheme.error,
                  ),
                  title: Text(
                    sheetCtx.tr('swipeDeleteLibrary'),
                    style: TextStyle(
                      color: Theme.of(sheetCtx).colorScheme.error,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  onTap: () {
                    Navigator.pop(sheetCtx);
                    confirmAndDeleteFromLibrary(
                      context,
                      ref,
                      song,
                      onDeleted: onChanged,
                    );
                  },
                ),
            ],
          ),
        ),
      ),
    );
  }

  static Future<void> _openContainingAlbumsSheet(
    BuildContext context,
    WidgetRef ref,
    Song song,
  ) async {
    await showTMusicSheet<void>(
      context,
      builder: (_) => _ContainingAlbumsSheet(song: song),
    );
  }
}

class _ContainingAlbumsSheet extends ConsumerStatefulWidget {
  const _ContainingAlbumsSheet({required this.song});
  final Song song;

  @override
  ConsumerState<_ContainingAlbumsSheet> createState() =>
      _ContainingAlbumsSheetState();
}

class _ContainingAlbumsSheetState
    extends ConsumerState<_ContainingAlbumsSheet> {
  List<Album> _albums = [];
  bool _busy = true;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      final targetIds = widget.song.albumIds.toSet();
      final matched = <Album>[];
      var page = 1;
      var more = true;
      while (more && matched.length < targetIds.length) {
        final res = await repo.albums(page: page);
        for (final a in res.items) {
          if (targetIds.contains(a.id)) matched.add(a);
        }
        more = res.hasMore;
        page++;
      }
      if (mounted) setState(() => _albums = matched);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => SafeArea(
    child: Padding(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            context.tr('inAlbums'),
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 10),
          if (_busy) const LinearProgressIndicator(),
          if (_error != null) Notice(errorText(context, _error!), error: true),
          if (!_busy && _albums.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 12),
              child: Text(context.tr('notInAnyAlbum')),
            ),
          for (final a in _albums)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Cover(url: a.coverImageUrl, size: 42, radius: 8),
              title: Text(a.name, maxLines: 1, overflow: TextOverflow.ellipsis),
              subtitle: Text(
                '${a.songCount} ${context.tr('songs').toLowerCase()}',
              ),
              trailing: const Icon(Icons.chevron_right_rounded),
              onTap: () {
                Navigator.pop(context);
                context.push('/albums/${a.id}');
              },
            ),
        ],
      ),
    ),
  );
}

/// Multi-album picker bottom sheet with Vietnamese search, duplicate protection,
/// existing-membership badges, and inline new-album creation.
class SongAlbumPickerSheet extends ConsumerStatefulWidget {
  const SongAlbumPickerSheet({super.key, required this.songs});
  final List<Song> songs;

  @override
  ConsumerState<SongAlbumPickerSheet> createState() =>
      _SongAlbumPickerSheetState();
}

class _SongAlbumPickerSheetState extends ConsumerState<SongAlbumPickerSheet> {
  final _search = TextEditingController();
  final _newAlbumName = TextEditingController();
  List<Album> _albums = [];
  final Set<String> _selectedAlbumIds = {};
  final Set<String> _alreadyMemberAlbumIds = {};
  bool _busy = true;
  bool _saving = false;
  bool _creatingInline = false;
  Object? _error;

  bool get _isSingle => widget.songs.length == 1;

  @override
  void initState() {
    super.initState();
    if (_isSingle) {
      _alreadyMemberAlbumIds.addAll(widget.songs.first.albumIds);
      _selectedAlbumIds.addAll(widget.songs.first.albumIds);
    }
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    _newAlbumName.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      final collected = <Album>[];
      var page = 1;
      var more = true;
      while (more && page <= 10) {
        final res = await repo.albums(page: page);
        collected.addAll(res.items);
        more = res.hasMore;
        page++;
      }
      if (mounted) {
        setState(() {
          _albums = collected;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _createInlineAlbum() async {
    final name = _newAlbumName.text.trim();
    if (name.isEmpty) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final created = await ref
          .read(libraryRepositoryProvider)
          .saveAlbum(name: name, description: '');
      ref.read(libraryRevisionProvider.notifier).changed();
      if (mounted) {
        _newAlbumName.clear();
        setState(() {
          _albums = [created, ..._albums];
          _selectedAlbumIds.add(created.id);
          _creatingInline = false;
        });
        TMusicHaptics.light();
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _saveSelection() async {
    final toAdd = _selectedAlbumIds.difference(_alreadyMemberAlbumIds);
    if (toAdd.isEmpty) {
      Navigator.pop(context, false);
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      for (final albumId in toAdd) {
        for (final song in widget.songs) {
          // Prevent adding a duplicate membership record into the same album.
          if (song.albumIds.contains(albumId)) continue;
          final freshAlbum = await repo.album(albumId);
          await repo.addToAlbum(freshAlbum, song);
        }
      }
      ref.read(libraryRevisionProvider.notifier).changed();
      TMusicHaptics.medium();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(context.tr('addedToAlbumNotice'))),
        );
        Navigator.pop(context, true);
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final q = _search.text.trim();
    final filtered = q.isEmpty
        ? _albums
        : _albums
              .where(
                (a) =>
                    matchesNormalizedQuery(a.name, q) ||
                    matchesNormalizedQuery(a.description ?? '', q),
              )
              .toList();
    final pendingAddCount = _selectedAlbumIds
        .difference(_alreadyMemberAlbumIds)
        .length;

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          bottom: MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: SizedBox(
          height: MediaQuery.sizeOf(context).height * 0.75,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 4, 20, 10),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            context.tr('chooseAlbums'),
                            style: Theme.of(context).textTheme.headlineSmall,
                          ),
                        ),
                        TextButton.icon(
                          key: const Key('album-picker-create-toggle'),
                          onPressed: _saving
                              ? null
                              : () => setState(
                                  () => _creatingInline = !_creatingInline,
                                ),
                          icon: Icon(
                            _creatingInline
                                ? Icons.close_rounded
                                : Icons.add_rounded,
                            size: 18,
                          ),
                          label: Text(
                            context.tr(
                              _creatingInline ? 'cancel' : 'createAlbumInline',
                            ),
                          ),
                        ),
                      ],
                    ),
                    if (_creatingInline) ...[
                      const SizedBox(height: 8),
                      Row(
                        children: [
                          Expanded(
                            child: TextField(
                              key: const Key('album-picker-new-name'),
                              controller: _newAlbumName,
                              autofocus: true,
                              maxLength: 120,
                              decoration: InputDecoration(
                                labelText: context.tr('albumName'),
                                counterText: '',
                              ),
                              onSubmitted: (_) => _createInlineAlbum(),
                            ),
                          ),
                          const SizedBox(width: 8),
                          FilledButton(
                            key: const Key('album-picker-create-submit'),
                            onPressed: _saving ? null : _createInlineAlbum,
                            child: Text(context.tr('save')),
                          ),
                        ],
                      ),
                    ],
                    const SizedBox(height: 10),
                    TextField(
                      key: const Key('album-picker-search'),
                      controller: _search,
                      onChanged: (_) => setState(() {}),
                      decoration: InputDecoration(
                        hintText: context.tr('searchAlbumsHint'),
                        prefixIcon: const Icon(Icons.search_rounded),
                        suffixIcon: _search.text.isEmpty
                            ? null
                            : IconButton(
                                onPressed: () {
                                  _search.clear();
                                  setState(() {});
                                },
                                icon: const Icon(Icons.close_rounded),
                              ),
                      ),
                    ),
                    if (_busy || _saving) ...[
                      const SizedBox(height: 8),
                      const LinearProgressIndicator(),
                    ],
                    if (_error != null) ...[
                      const SizedBox(height: 8),
                      Notice(errorText(context, _error!), error: true),
                    ],
                  ],
                ),
              ),
              Expanded(
                child: filtered.isEmpty && !_busy
                    ? Center(
                        child: Padding(
                          padding: const EdgeInsets.all(20),
                          child: Text(
                            context.tr(
                              _albums.isEmpty ? 'noAlbum' : 'emptySearch',
                            ),
                            style: Theme.of(context).textTheme.bodyMedium,
                          ),
                        ),
                      )
                    : ListView.builder(
                        padding: const EdgeInsets.symmetric(horizontal: 14),
                        itemCount: filtered.length,
                        itemBuilder: (context, index) {
                          final a = filtered[index];
                          final alreadyIn = _alreadyMemberAlbumIds.contains(
                            a.id,
                          );
                          final checked = _selectedAlbumIds.contains(a.id);
                          return CheckboxListTile(
                            key: Key('album-picker-item-${a.id}'),
                            contentPadding: const EdgeInsets.symmetric(
                              horizontal: 6,
                              vertical: 2,
                            ),
                            secondary: Cover(
                              url: a.coverImageUrl,
                              size: 44,
                              radius: 8,
                            ),
                            title: Text(
                              a.name,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                            subtitle: Text(
                              alreadyIn
                                  ? context.tr('albumPickerInAlbum')
                                  : '${a.songCount} ${context.tr('songs').toLowerCase()}',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                            value: checked,
                            onChanged: _saving || alreadyIn
                                ? null
                                : (v) {
                                    TMusicHaptics.selection();
                                    setState(() {
                                      if (v == true) {
                                        _selectedAlbumIds.add(a.id);
                                      } else {
                                        _selectedAlbumIds.remove(a.id);
                                      }
                                    });
                                  },
                          );
                        },
                      ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 10, 20, 14),
                child: FilledButton(
                  key: const Key('album-picker-confirm'),
                  onPressed: _saving || pendingAddCount == 0
                      ? null
                      : _saveSelection,
                  child: Text(
                    pendingAddCount > 0
                        ? '${context.tr('addToAlbum')} ($pendingAddCount)'
                        : context.tr('save'),
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
