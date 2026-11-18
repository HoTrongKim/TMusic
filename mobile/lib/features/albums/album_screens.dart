import 'dart:typed_data';

import 'package:audio_service/audio_service.dart';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../app/theme/tmusic_theme.dart';
import '../../core/api/api_client.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../shared/models/library_models.dart';
import '../library/library_repository.dart';
import '../library/library_screen.dart';
import '../playback/local_audio.dart';
import '../playback/local_audio_screen.dart';
import '../share/shared_links.dart';
import 'album_share_sheets.dart';

/// Create & Edit Album Screen (with Custom Cover Upload & Studio Palette)
class AlbumEditor extends ConsumerStatefulWidget {
  const AlbumEditor({super.key, this.id});
  final String? id;

  @override
  ConsumerState<AlbumEditor> createState() => _AlbumEditorState();
}

class _AlbumEditorState extends ConsumerState<AlbumEditor> {
  static const _presetColors = [
    'navy',
    'blue',
    'green',
    'terracotta',
    'slate',
    'amber',
  ];

  final _form = GlobalKey<FormState>();
  final _name = TextEditingController(),
      _description = TextEditingController(),
      _cover = TextEditingController();
  Album? _album;
  String? _pickedFileName;
  Uint8List? _pickedBytes;
  bool _removeUploadedCover = false;
  bool _busy = false;
  Object? _error;

  @override
  void initState() {
    super.initState();
    if (widget.id != null) _load();
  }

  Future<void> _load() async {
    setState(() => _busy = true);
    try {
      final a = await ref.read(libraryRepositoryProvider).album(widget.id!);
      if (mounted) {
        setState(() {
          _album = a;
          _name.text = a.name;
          _description.text = a.description ?? '';
          _cover.text = a.coverImageUrl ?? '';
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _cover.dispose();
    super.dispose();
  }

  Future<void> _pickCoverImage() async {
    try {
      final file = await openFile(
        acceptedTypeGroups: const [
          XTypeGroup(
            label: 'Images',
            extensions: ['png', 'jpg', 'jpeg', 'webp'],
            mimeTypes: ['image/png', 'image/jpeg', 'image/webp'],
          ),
        ],
      );
      if (file == null) return;
      final bytes = await file.readAsBytes();
      if (bytes.isEmpty || bytes.length > 2 * 1024 * 1024) {
        if (mounted) {
          setState(
            () => _error = FormatException(context.tr('coverSecurityNote')),
          );
        }
        return;
      }
      if (mounted) {
        setState(() {
          _pickedFileName = file.name;
          _pickedBytes = bytes;
          _removeUploadedCover = false;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    }
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      var saved = await repo.saveAlbum(
        existing: _album,
        name: _name.text.trim(),
        description: _description.text.trim(),
        cover: _removeUploadedCover
            ? null
            : (_cover.text.trim().isEmpty ? null : _cover.text.trim()),
      );
      if (_pickedBytes != null && _pickedFileName != null) {
        Cover.evict(_album?.coverImageUrl);
        saved = await repo.uploadAlbumCover(
          saved,
          fileName: _pickedFileName!,
          bytes: _pickedBytes!,
        );
      } else if (_removeUploadedCover &&
          _album?.coverImageUrl?.startsWith('/api/v1/covers/') == true) {
        Cover.evict(_album?.coverImageUrl);
      }
      ref.read(libraryRevisionProvider.notifier).changed();
      if (mounted) context.pop(saved);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tokens = Theme.of(context).extension<TMusicTokens>();
    final hasUploaded =
        !_removeUploadedCover &&
        _pickedBytes == null &&
        _cover.text.startsWith('/api/v1/covers/');

    return Scaffold(
      appBar: AppBar(
        title: Text(
          context.tr(widget.id == null ? 'createAlbum' : 'editAlbum'),
        ),
      ),
      body: Form(
        key: _form,
        child: PageList(
          children: [
            if (_busy) const LinearProgressIndicator(),
            if (_error != null)
              Notice(
                _error is FormatException
                    ? (_error! as FormatException).message
                    : errorText(context, _error!),
                error: true,
              ),
            Center(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(18),
                  boxShadow: tokens?.heroShadow,
                ),
                child: _pickedBytes != null
                    ? ClipRRect(
                        borderRadius: BorderRadius.circular(18),
                        child: Image.memory(
                          _pickedBytes!,
                          width: 136,
                          height: 136,
                          fit: BoxFit.cover,
                          errorBuilder: (_, _, _) =>
                              const Cover(size: 136, radius: 18),
                        ),
                      )
                    : Cover(
                        url: _removeUploadedCover ? null : _cover.text,
                        size: 136,
                        radius: 18,
                      ),
              ),
            ),
            const SizedBox(height: 14),
            Wrap(
              alignment: WrapAlignment.center,
              spacing: 8,
              runSpacing: 8,
              children: [
                FilledButton.tonalIcon(
                  onPressed: _busy ? null : _pickCoverImage,
                  icon: const Icon(Icons.image_outlined, size: 18),
                  label: Text(context.tr('uploadCover')),
                ),
                if (hasUploaded || _pickedBytes != null)
                  TextButton.icon(
                    onPressed: _busy
                        ? null
                        : () => setState(() {
                            _pickedBytes = null;
                            _pickedFileName = null;
                            if (_cover.text.startsWith('/api/v1/covers/')) {
                              _removeUploadedCover = true;
                              _cover.clear();
                            }
                          }),
                    icon: const Icon(Icons.close_rounded, size: 18),
                    label: Text(context.tr('removeCover')),
                  ),
              ],
            ),
            const SizedBox(height: 12),
            Wrap(
              alignment: WrapAlignment.center,
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final color in _presetColors)
                  ChoiceChip(
                    showCheckmark: false,
                    avatar: Cover(url: 'cover:$color', size: 20, radius: 10),
                    label: Text(context.tr('cover_$color')),
                    selected:
                        _pickedBytes == null && _cover.text == 'cover:$color',
                    onSelected: (_) => setState(() {
                      _pickedBytes = null;
                      _pickedFileName = null;
                      _removeUploadedCover = false;
                      _cover.text = 'cover:$color';
                    }),
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              context.tr('coverSecurityNote'),
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 20),
            TextFormField(
              key: const Key('album-name'),
              controller: _name,
              maxLength: 120,
              decoration: InputDecoration(labelText: context.tr('albumName')),
              validator: (v) => requiredText(context, v),
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _description,
              maxLength: 1000,
              minLines: 2,
              maxLines: 4,
              decoration: InputDecoration(labelText: context.tr('description')),
            ),
            const SizedBox(height: 22),
            FilledButton(
              key: const Key('album-save'),
              onPressed: _busy || (widget.id != null && _album == null)
                  ? null
                  : _save,
              child: Text(context.tr('save')),
            ),
          ],
        ),
      ),
    );
  }
}

/// Cross & Matchy-inspired Album Detail Screen:
/// Hero Cover Stage + Asymmetric Action Bar (Shuffle & Prominent Solid Play Circle)
/// + Top-Rounded 24dp Numbered Tracklist Sheet.
class AlbumDetailScreen extends ConsumerStatefulWidget {
  const AlbumDetailScreen({super.key, required this.id});
  final String id;

  @override
  ConsumerState<AlbumDetailScreen> createState() => _AlbumDetailScreenState();
}

class _AlbumDetailScreenState extends ConsumerState<AlbumDetailScreen> {
  Album? _album;
  List<Song> _songs = [];
  Set<String> _localSongIds = {};
  LocalAudio? _player;
  Object? _error;
  bool _busy = true, _more = false;
  int _page = 1;
  int _loadRequest = 0;

  Future<void> _playAlbumMp3(
    Album a, {
    String? startSongId,
    bool shuffle = false,
  }) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final local = await ref.read(localLibraryProvider.future);
      final repo = ref.read(libraryRepositoryProvider);
      final tracks = <LocalTrack>[];
      var startIndex = 0;
      final songs = [..._songs];
      var page = _page;
      var more = _more;
      while (more) {
        try {
          final next = await repo.songs(albumId: a.id, page: ++page);
          if (!mounted) return;
          songs.addAll(next.items);
          more = next.hasMore;
        } on ApiFailure catch (e) {
          if (e.status != null) rethrow;
          break;
        }
      }
      for (final song in songs) {
        LocalTrack? resolved = await local.getDownloadedSongTrack(song);
        if (resolved == null && !song.isGuestLocal && song.audioReady) {
          try {
            resolved = await local.ensureSongTrack(
              song,
              () => repo.fetchSongAudioBytes(song.id),
            );
          } catch (_) {}
        }
        if (resolved != null) {
          if (song.id == startSongId) startIndex = tracks.length;
          tracks.add(resolved);
        }
      }
      if (tracks.isEmpty) {
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text(context.tr('emptyDownloaded'))),
          );
        }
        return;
      }
      if (!mounted) return;
      final player = await ref.read(localAudioProvider.future);
      TMusicHaptics.light();
      await player.start(tracks, startIndex, albumName: a.name);
      await player.setShuffleMode(
        shuffle ? AudioServiceShuffleMode.all : AudioServiceShuffleMode.none,
      );
      if (mounted) setState(() => _player = player);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({bool append = false}) async {
    final request = ++_loadRequest;
    final requestedPage = append ? _page + 1 : 1;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(libraryRepositoryProvider);
      final a = await repo.album(widget.id);
      final page = await repo.songs(albumId: widget.id, page: requestedPage);
      if (!mounted || request != _loadRequest) return;
      final localIds = <String>{};
      try {
        final local = await ref.read(localLibraryProvider.future);
        for (final song in page.items) {
          if (await local.hasDownloadedSong(song.id)) localIds.add(song.id);
        }
      } catch (_) {}
      if (mounted && request == _loadRequest) {
        setState(() {
          _album = a;
          _localSongIds = append ? {..._localSongIds, ...localIds} : localIds;
          _songs = append ? [..._songs, ...page.items] : page.items;
          _more = page.hasMore;
          _page = requestedPage;
        });
      }
    } catch (e) {
      if (mounted && request == _loadRequest) setState(() => _error = e);
    } finally {
      if (mounted && request == _loadRequest) setState(() => _busy = false);
    }
  }

  bool _canPlay(Song song) =>
      _localSongIds.contains(song.id) ||
      (!song.isGuestLocal && song.audioReady);

  Future<void> _mutate(Future<void> Function() action) async {
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

  Future<void> _move(int index, int direction) async {
    final ids = _songs.map((s) => s.id).toList();
    final moved = ids.removeAt(index);
    ids.insert(index + direction, moved);
    await _mutate(
      () => ref.read(libraryRepositoryProvider).order(_album!, ids),
    );
  }

  Future<void> _pickExistingSong(Album a) async {
    await showTMusicSheet<bool>(
      context,
      builder: (_) => ExistingSongPicker(
        album: a,
        excludeIds: _songs.map((s) => s.id).toSet(),
      ),
    );
    if (mounted) await _load();
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(libraryRevisionProvider, (_, _) => _load());
    final a = _album;
    final scheme = Theme.of(context).colorScheme;
    final tokens = Theme.of(context).extension<TMusicTokens>();
    final globalAudio = ref.watch(localAudioProvider);
    final activeGlobal = globalAudio.hasValue ? globalAudio.value : null;
    final inlinePlayer =
        _player ??
        (a != null && activeGlobal?.mediaItem.value?.album == a.name
            ? activeGlobal
            : null);
    final hasPlayableMp3 = _more || _songs.any(_canPlay);
    final ambient = albumAmbientColor(
      a?.coverImageUrl ?? a?.id,
      isDark: scheme.brightness == Brightness.dark,
    );

    return Scaffold(
      appBar: AppBar(
        title: Text(a?.name ?? context.tr('albums')),
        actions: [
          if (a != null) ...[
            IconButton(
              tooltip: context.tr('editAlbum'),
              onPressed: () => context.push('/albums/${a.id}/edit'),
              icon: const Icon(Icons.edit_outlined),
            ),
            IconButton(
              tooltip: context.tr('delete'),
              onPressed: _busy
                  ? null
                  : () async {
                      if (!await confirmAction(
                        context,
                        context.tr('albumDeleteTitle'),
                        context.tr('albumDeleteBody'),
                      )) {
                        return;
                      }
                      try {
                        await ref
                            .read(libraryRepositoryProvider)
                            .deleteAlbum(a);
                        ref.read(libraryRevisionProvider.notifier).changed();
                        if (context.mounted) context.pop();
                      } catch (e) {
                        if (mounted) setState(() => _error = e);
                      }
                    },
              icon: const Icon(Icons.delete_outline_rounded),
            ),
          ],
        ],
      ),
      bottomNavigationBar: inlinePlayer == null
          ? const SafeArea(top: false, child: MiniPlayerDock())
          : null,
      body: RefreshIndicator(
        onRefresh: _load,
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topCenter,
                    end: Alignment.bottomCenter,
                    colors: [ambient, scheme.surfaceContainerLowest],
                  ),
                ),
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 20, 18),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const OfflineBanner(),
                      if (_busy) const LinearProgressIndicator(),
                      if (_error != null) ...[
                        Notice(errorText(context, _error!), error: true),
                        OutlinedButton(
                          onPressed: _load,
                          child: Text(context.tr('retry')),
                        ),
                      ],
                      if (a != null) ...[
                        Center(
                          child: DecoratedBox(
                            decoration: BoxDecoration(
                              borderRadius: BorderRadius.circular(8),
                              boxShadow: tokens?.heroShadow,
                            ),
                            child: Cover(
                              url: a.coverImageUrl,
                              size: 196,
                              radius: 8,
                            ),
                          ),
                        ),
                        const SizedBox(height: 20),
                        Text(
                          a.name,
                          style: Theme.of(context).textTheme.headlineLarge
                              ?.copyWith(fontWeight: FontWeight.w800),
                        ),
                        if (a.description?.isNotEmpty == true) ...[
                          const SizedBox(height: 6),
                          Text(a.description!),
                        ],
                        const SizedBox(height: 6),
                        Wrap(
                          spacing: 10,
                          runSpacing: 6,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            Text(
                              '${a.songCount} ${context.tr('songs').toLowerCase()}',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                            if (a.isGuestLocal)
                              Chip(
                                avatar: const Icon(Icons.phone_android_rounded),
                                label: Text(context.tr('savedOnDevice')),
                                visualDensity: VisualDensity.compact,
                              ),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Wrap(
                          alignment: WrapAlignment.spaceBetween,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            Wrap(
                              spacing: 6,
                              runSpacing: 6,
                              crossAxisAlignment: WrapCrossAlignment.center,
                              children: [
                                IconButton(
                                  tooltip: context.tr(
                                    a.isFavorite
                                        ? 'favoriteRemove'
                                        : 'favoriteAdd',
                                  ),
                                  onPressed: _busy
                                      ? null
                                      : () => _mutate(() async {
                                          await ref
                                              .read(libraryRepositoryProvider)
                                              .saveAlbum(
                                                existing: a,
                                                name: a.name,
                                                description:
                                                    a.description ?? '',
                                                cover: a.coverImageUrl,
                                                favorite: !a.isFavorite,
                                              );
                                        }),
                                  icon: Icon(
                                    a.isFavorite
                                        ? Icons.favorite_rounded
                                        : Icons.favorite_border_rounded,
                                    color: a.isFavorite
                                        ? const Color(0xFFE05263)
                                        : null,
                                  ),
                                ),
                                IconButton(
                                  tooltip: context.tr('quickAdd'),
                                  onPressed: () {
                                    ref
                                        .read(linkDraftProvider.notifier)
                                        .update(albumId: a.id);
                                    context.go('/?album=${a.id}');
                                  },
                                  icon: const Icon(Icons.add_link_rounded),
                                ),
                                IconButton(
                                  key: const Key('album-share'),
                                  tooltip: context.tr('shareAlbum'),
                                  onPressed: () =>
                                      openShareAlbumSheet(context, a),
                                  icon: const Icon(Icons.ios_share_rounded),
                                ),
                                IconButton(
                                  tooltip: context.tr('addExistingSong'),
                                  onPressed: _busy
                                      ? null
                                      : () => _pickExistingSong(a),
                                  icon: const Icon(Icons.playlist_add_rounded),
                                ),
                              ],
                            ),
                            Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                IconButton(
                                  tooltip: context.tr('shuffle'),
                                  onPressed: _busy || !hasPlayableMp3
                                      ? null
                                      : () => _playAlbumMp3(a, shuffle: true),
                                  icon: const Icon(Icons.shuffle_rounded),
                                ),
                                Tooltip(
                                  message: context.tr('playAlbumMp3'),
                                  child: SizedBox.square(
                                    dimension: 56,
                                    child: FilledButton(
                                      key: const Key('album-play-mp3'),
                                      style: FilledButton.styleFrom(
                                        backgroundColor:
                                            TMusicTheme.spotifyGreen,
                                        foregroundColor: Colors.black,
                                        shape: const CircleBorder(),
                                        padding: EdgeInsets.zero,
                                      ),
                                      onPressed: _busy || !hasPlayableMp3
                                          ? null
                                          : () => _playAlbumMp3(a),
                                      child: const Icon(
                                        Icons.play_arrow_rounded,
                                        size: 34,
                                      ),
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          ],
                        ),
                        if (inlinePlayer != null) ...[
                          const SizedBox(height: 14),
                          LocalPlayerControls(inlinePlayer),
                        ],
                      ],
                    ],
                  ),
                ),
              ),
            ),
            if (a != null && !_busy && _songs.isEmpty)
              SliverPadding(
                padding: const EdgeInsets.symmetric(horizontal: 20),
                sliver: SliverToBoxAdapter(
                  child: EmptyView(
                    icon: Icons.music_note_rounded,
                    title: context.tr('emptyLibrary'),
                    body: context.tr('emptyLibraryBody'),
                  ),
                ),
              )
            else if (_songs.isNotEmpty)
              SliverPadding(
                padding: const EdgeInsets.symmetric(horizontal: 14),
                sliver: SliverToBoxAdapter(
                  child: Material(
                    color: scheme.surface,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(22),
                      side: BorderSide(
                        color: scheme.outlineVariant.withValues(alpha: 0.6),
                      ),
                    ),
                    clipBehavior: Clip.antiAlias,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8,
                        vertical: 8,
                      ),
                      child: Column(
                        children: [
                          for (final (index, song) in _songs.indexed)
                            SongTile(
                              song,
                              trackNumber: index + 1,
                              isDownloaded: _localSongIds.contains(song.id),
                              trailing: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  if (_canPlay(song))
                                    IconButton(
                                      visualDensity: VisualDensity.compact,
                                      tooltip: context.tr('play'),
                                      onPressed: _busy || a == null
                                          ? null
                                          : () => _playAlbumMp3(
                                              a,
                                              startSongId: song.id,
                                            ),
                                      icon: Icon(
                                        Icons.play_circle_fill_rounded,
                                        color: scheme.primary,
                                        size: 26,
                                      ),
                                    ),
                                  PopupMenuButton<String>(
                                    tooltip: context.tr('editSong'),
                                    onSelected: (value) {
                                      if (value == 'remove') {
                                        _mutate(
                                          () => ref
                                              .read(libraryRepositoryProvider)
                                              .removeFromAlbum(a!, song),
                                        );
                                      } else {
                                        _move(index, value == 'up' ? -1 : 1);
                                      }
                                    },
                                    itemBuilder: (context) => [
                                      PopupMenuItem(
                                        value: 'remove',
                                        child: Text(context.tr('removeAlbum')),
                                      ),
                                      if (!_more && index > 0)
                                        PopupMenuItem(
                                          value: 'up',
                                          child: Text(context.tr('moveUp')),
                                        ),
                                      if (!_more && index < _songs.length - 1)
                                        PopupMenuItem(
                                          value: 'down',
                                          child: Text(context.tr('moveDown')),
                                        ),
                                    ],
                                  ),
                                ],
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            if (_more)
              SliverPadding(
                padding: const EdgeInsets.all(20),
                sliver: SliverToBoxAdapter(
                  child: OutlinedButton(
                    onPressed: _busy ? null : () => _load(append: true),
                    child: Text(context.tr('more')),
                  ),
                ),
              ),
            const SliverToBoxAdapter(child: SizedBox(height: 36)),
          ],
        ),
      ),
    );
  }
}

class ExistingSongPicker extends ConsumerStatefulWidget {
  const ExistingSongPicker({
    super.key,
    required this.album,
    required this.excludeIds,
  });
  final Album album;
  final Set<String> excludeIds;

  @override
  ConsumerState<ExistingSongPicker> createState() => _ExistingSongPickerState();
}

class _ExistingSongPickerState extends ConsumerState<ExistingSongPicker> {
  List<Song> _items = [];
  final _selectedIds = <String>{};
  final _addedIds = <String>{};
  final _alreadyInAlbumIds = <String>{};
  final _itemErrors = <String, Object>{};
  final _sourceErrors = <String, Object>{};
  bool _busy = true, _more = false;
  bool _saving = false;
  String? _addingId;
  int _page = 0;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({bool append = false}) async {
    final requestedPage = append ? _page + 1 : 1;
    setState(() => _busy = true);
    try {
      final res = await ref
          .read(libraryRepositoryProvider)
          .songs(page: requestedPage);
      if (mounted) {
        setState(() {
          final byId = {
            if (append)
              for (final song in _items) song.id: song,
            for (final song in res.items) song.id: song,
          };
          _items = byId.values.toList();
          _page = requestedPage;
          _more = res.hasMore;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  bool _inAlbum(Song song) =>
      widget.excludeIds.contains(song.id) ||
      _alreadyInAlbumIds.contains(song.id) ||
      _addedIds.contains(song.id) ||
      song.albumIds.contains(widget.album.id);

  void _toggle(Song song) {
    if (_saving || _inAlbum(song)) return;
    setState(() {
      if (!_selectedIds.add(song.id)) _selectedIds.remove(song.id);
    });
  }

  Future<void> _addSelected() async {
    if (_saving || _selectedIds.isEmpty) return;
    final repo = ref.read(libraryRepositoryProvider);
    final revision = ref.read(libraryRevisionProvider.notifier);
    final pending = _items.where((s) => _selectedIds.contains(s.id)).toList();
    setState(() => _saving = true);
    try {
      for (final song in pending) {
        if (!mounted) break;
        setState(() {
          _addingId = song.id;
          _itemErrors.remove(song.id);
        });
        try {
          // Adding a song changes the album's concurrency token. Fetch both
          // current models before each write, also preserving guest memberships.
          final album = await repo.album(widget.album.id);
          final current = await repo.song(song.id);
          if (!mounted) break;
          final alreadyPresent = current.albumIds.contains(album.id);
          if (!alreadyPresent) {
            await repo.addToAlbum(album, current);
            // Notify even if the sheet was dismissed during the request.
            revision.changed();
          }
          if (!mounted) break;
          setState(() {
            _selectedIds.remove(song.id);
            if (alreadyPresent) {
              _alreadyInAlbumIds.add(song.id);
            } else {
              _addedIds.add(song.id);
            }
          });
        } catch (e) {
          if (mounted) setState(() => _itemErrors[song.id] = e);
        }
      }
    } finally {
      if (mounted) {
        setState(() {
          _saving = false;
          _addingId = null;
        });
      }
    }
  }

  Future<void> _openSource(Song song) async {
    final value = SharedLinks.supported(song.originalUrl)
        ? song.originalUrl
        : song.url;
    if (!SharedLinks.supported(value)) return;
    try {
      if (!await launchUrl(
        Uri.parse(value),
        mode: LaunchMode.externalApplication,
      )) {
        throw const ApiFailure('source_unavailable');
      }
      if (mounted) setState(() => _sourceErrors.remove(song.id));
    } catch (e) {
      if (mounted) setState(() => _sourceErrors[song.id] = e);
    }
  }

  Widget _songRow(BuildContext context, Song song) {
    final scheme = Theme.of(context).colorScheme;
    final inAlbum = _inAlbum(song);
    final selected = _selectedIds.contains(song.id);
    final adding = _addingId == song.id;
    final error = _itemErrors[song.id];
    final sourceSupported =
        SharedLinks.supported(song.originalUrl) ||
        SharedLinks.supported(song.url);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Material(
        color: selected
            ? scheme.primary.withValues(alpha: 0.1)
            : scheme.surfaceContainerLow,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
          side: BorderSide(
            color: selected ? scheme.primary : scheme.outlineVariant,
          ),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          key: Key('album-picker-row-${song.id}'),
          onTap: _saving || inAlbum ? null : () => _toggle(song),
          excludeFromSemantics: true,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(0, 10, 10, 8),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Checkbox(
                  key: Key('album-picker-select-${song.id}'),
                  value: selected || inAlbum,
                  semanticLabel:
                      '${song.title}, ${song.sourceAttribution}'
                      '${inAlbum ? ', ${context.tr('albumPickerInAlbum')}' : ''}',
                  materialTapTargetSize: MaterialTapTargetSize.padded,
                  visualDensity: VisualDensity.standard,
                  onChanged: _saving || inAlbum ? null : (_) => _toggle(song),
                ),
                Padding(
                  padding: const EdgeInsets.only(top: 4, right: 10),
                  child: Cover(url: song.thumbnail, size: 40, radius: 6),
                ),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        song.title,
                        maxLines: 3,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        song.sourceAttribution,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      if (adding)
                        Semantics(
                          liveRegion: true,
                          child: Padding(
                            key: Key('album-picker-busy-${song.id}'),
                            padding: const EdgeInsets.symmetric(vertical: 8),
                            child: Row(
                              children: [
                                const SizedBox.square(
                                  dimension: 18,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                ),
                                const SizedBox(width: 8),
                                Expanded(
                                  child: Text(context.tr('albumPickerAdding')),
                                ),
                              ],
                            ),
                          ),
                        )
                      else if (inAlbum)
                        Padding(
                          padding: const EdgeInsets.only(top: 6),
                          child: Text(
                            context.tr(
                              _addedIds.contains(song.id)
                                  ? 'albumPickerAdded'
                                  : 'albumPickerInAlbum',
                            ),
                            style: Theme.of(context).textTheme.labelMedium
                                ?.copyWith(color: scheme.primary),
                          ),
                        ),
                      if (error != null)
                        Semantics(
                          liveRegion: true,
                          child: Padding(
                            key: Key('album-picker-error-${song.id}'),
                            padding: const EdgeInsets.only(top: 6),
                            child: Text(
                              '${context.tr('albumPickerAddFailed')}. ${errorText(context, error)}',
                              style: TextStyle(color: scheme.error),
                            ),
                          ),
                        ),
                      if (_sourceErrors[song.id] case final sourceError?)
                        Text(
                          errorText(context, sourceError),
                          style: TextStyle(color: scheme.error),
                        ),
                      if (sourceSupported)
                        Align(
                          alignment: Alignment.centerLeft,
                          child: TextButton(
                            onPressed: () => _openSource(song),
                            style: TextButton.styleFrom(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 4,
                                vertical: 8,
                              ),
                              minimumSize: const Size(48, 48),
                            ),
                            child: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                const Icon(Icons.open_in_new_rounded, size: 16),
                                const SizedBox(width: 6),
                                Flexible(child: Text(context.tr('openSource'))),
                              ],
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_saving,
    child: SafeArea(
      top: false,
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * 0.82,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 4, 8, 8),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Text(
                      context.tr('addExistingSong'),
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                  IconButton(
                    tooltip: context.tr(
                      _addedIds.isEmpty ? 'cancel' : 'albumPickerDone',
                    ),
                    onPressed: _saving
                        ? null
                        : () => Navigator.pop(context, _addedIds.isNotEmpty),
                    icon: const Icon(Icons.close_rounded),
                  ),
                ],
              ),
            ),
            if (_busy) const LinearProgressIndicator(),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                children: [
                  Text(context.tr('albumPickerHint')),
                  const SizedBox(height: 12),
                  if (_error != null) ...[
                    Notice(errorText(context, _error!), error: true),
                    TextButton(
                      onPressed: _busy || _saving
                          ? null
                          : () => _load(append: _page > 0),
                      child: Text(context.tr('retry')),
                    ),
                  ],
                  if (!_busy && _items.isEmpty)
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 20),
                      child: Text(context.tr('emptySearch')),
                    ),
                  ..._items.map((song) => _songRow(context, song)),
                  if (_more)
                    OutlinedButton(
                      onPressed: _busy || _saving
                          ? null
                          : () => _load(append: true),
                      child: Text(context.tr('more')),
                    ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Semantics(
                    liveRegion: true,
                    child: Text(
                      context
                          .tr('albumPickerSelected')
                          .replaceAll('{count}', '${_selectedIds.length}'),
                    ),
                  ),
                  const SizedBox(height: 8),
                  FilledButton(
                    key: const Key('album-picker-add'),
                    onPressed: _saving || _busy || _selectedIds.isEmpty
                        ? null
                        : _addSelected,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 10),
                      child: Text(
                        context
                            .tr('albumPickerAddSelected')
                            .replaceAll('{count}', '${_selectedIds.length}'),
                        textAlign: TextAlign.center,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );
}
