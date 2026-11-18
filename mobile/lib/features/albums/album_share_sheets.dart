import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/motion.dart';
import '../../shared/models/library_models.dart';
import '../library/library_repository.dart';

Future<void> openShareAlbumSheet(BuildContext context, Album album) {
  return showTMusicSheet<void>(
    context,
    builder: (_) => _ShareAlbumSheet(album: album),
  );
}

Future<Album?> openImportSharedAlbumSheet(BuildContext context) {
  return showTMusicSheet<Album>(
    context,
    builder: (_) => const _ImportSharedAlbumSheet(),
  );
}

class _ShareAlbumSheet extends ConsumerStatefulWidget {
  const _ShareAlbumSheet({required this.album});
  final Album album;

  @override
  ConsumerState<_ShareAlbumSheet> createState() => _ShareAlbumSheetState();
}

class _ShareAlbumSheetState extends ConsumerState<_ShareAlbumSheet> {
  AlbumShareInfo? _share;
  bool _busy = true;
  Object? _error;
  String? _notice;

  @override
  void initState() {
    super.initState();
    _initShare();
  }

  Future<void> _initShare() async {
    final isGuest =
        ref.read(sessionProvider).value == null || widget.album.isGuestLocal;
    if (isGuest) {
      if (mounted) setState(() => _busy = false);
      return;
    }
    try {
      final info = await ref
          .read(libraryRepositoryProvider)
          .createAlbumShare(widget.album.id);
      if (mounted) setState(() => _share = info);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _revoke() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref
          .read(libraryRepositoryProvider)
          .revokeAlbumShare(widget.album.id);
      if (mounted) {
        setState(() {
          _share = null;
          _notice = context.tr('shareRevoked');
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final isGuest =
        ref.watch(sessionProvider).value == null || widget.album.isGuestLocal;
    final scheme = Theme.of(context).colorScheme;

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 6, 20, 24),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Cover(url: widget.album.coverImageUrl, size: 48, radius: 6),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          context.tr('shareAlbum'),
                          style: Theme.of(context).textTheme.headlineSmall,
                        ),
                        Text(
                          widget.album.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              if (_busy) const LinearProgressIndicator(),
              if (_error != null)
                Notice(errorText(context, _error!), error: true),
              if (_notice != null) Notice(_notice!),
              if (isGuest) ...[
                Notice(context.tr('guestShareLoginHint')),
                const SizedBox(height: 12),
                FilledButton.icon(
                  onPressed: () {
                    Navigator.pop(context);
                    context.push('/auth/login');
                  },
                  icon: const Icon(Icons.login_rounded),
                  label: Text(context.tr('guestLoginCta')),
                ),
              ] else if (_share != null) ...[
                Text(
                  context.tr('shareCodeHint'),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 12),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 18,
                    vertical: 16,
                  ),
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerLow,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: SelectableText(
                    _share!.shareCode,
                    key: const Key('album-share-code-text'),
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.headlineLarge
                        ?.copyWith(letterSpacing: 4, color: scheme.primary),
                  ),
                ),
                const SizedBox(height: 14),
                FilledButton.icon(
                  key: const Key('copy-share-code-button'),
                  onPressed: () async {
                    await Clipboard.setData(
                      ClipboardData(text: _share!.shareCode),
                    );
                    if (context.mounted) {
                      setState(() => _notice = context.tr('shareCodeCopied'));
                    }
                  },
                  icon: const Icon(Icons.copy_rounded, size: 18),
                  label: Text(context.tr('copyShareCode')),
                ),
                const SizedBox(height: 8),
                TextButton.icon(
                  style: TextButton.styleFrom(foregroundColor: scheme.error),
                  onPressed: _busy ? null : _revoke,
                  icon: const Icon(Icons.link_off_rounded, size: 18),
                  label: Text(context.tr('revokeShareCode')),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _ImportSharedAlbumSheet extends ConsumerStatefulWidget {
  const _ImportSharedAlbumSheet();

  @override
  ConsumerState<_ImportSharedAlbumSheet> createState() =>
      _ImportSharedAlbumSheetState();
}

class _ImportSharedAlbumSheetState
    extends ConsumerState<_ImportSharedAlbumSheet> {
  final _codeCtrl = TextEditingController();
  SharedAlbumPreview? _preview;
  bool _busy = false;
  Object? _error;

  @override
  void dispose() {
    _codeCtrl.dispose();
    super.dispose();
  }

  Future<void> _lookup() async {
    final code = _codeCtrl.text.trim();
    if (code.isEmpty) return;
    setState(() {
      _busy = true;
      _error = null;
      _preview = null;
    });
    try {
      final p = await ref
          .read(libraryRepositoryProvider)
          .previewSharedAlbum(code);
      if (mounted) setState(() => _preview = p);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _importNow() async {
    final p = _preview;
    if (p == null) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final imported = await ref
          .read(libraryRepositoryProvider)
          .importSharedAlbum(p.shareCode);
      ref.read(libraryRevisionProvider.notifier).changed();
      if (mounted) Navigator.pop(context, imported);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final p = _preview;

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          6,
          20,
          24 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                context.tr('importSharedAlbum'),
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const SizedBox(height: 12),
              TextField(
                key: const Key('enter-share-code-input'),
                controller: _codeCtrl,
                enabled: !_busy,
                onChanged: (_) {
                  if (_preview != null || _error != null) {
                    setState(() {
                      _preview = null;
                      _error = null;
                    });
                  }
                },
                textCapitalization: TextCapitalization.characters,
                autocorrect: false,
                onSubmitted: (_) => _lookup(),
                decoration: InputDecoration(
                  hintText: context.tr('enterShareCode'),
                  prefixIcon: const Icon(Icons.tag_rounded),
                ),
              ),
              const SizedBox(height: 10),
              OutlinedButton.icon(
                key: const Key('preview-shared-album-button'),
                onPressed: _busy ? null : _lookup,
                icon: const Icon(Icons.search_rounded, size: 18),
                label: Text(context.tr('previewSharedBtn')),
              ),
              if (_busy) ...[
                const SizedBox(height: 10),
                const LinearProgressIndicator(),
              ],
              if (_error != null)
                Notice(errorText(context, _error!), error: true),
              if (p != null) ...[
                const SizedBox(height: 14),
                Container(
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerLow,
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          Cover(url: p.coverImageUrl, size: 56, radius: 6),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  p.albumName,
                                  style: Theme.of(context).textTheme.titleMedium
                                      ?.copyWith(fontWeight: FontWeight.w800),
                                ),
                                Text(
                                  '${context.tr('sharedByPrefix')}: ${p.sharedBy} • ${p.songCount} ${context.tr('songs').toLowerCase()}',
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                      if (p.songs.isNotEmpty) ...[
                        const SizedBox(height: 10),
                        ConstrainedBox(
                          constraints: const BoxConstraints(maxHeight: 190),
                          child: ListView.builder(
                            shrinkWrap: true,
                            itemCount: p.songs.length,
                            itemBuilder: (context, i) {
                              final item = p.songs[i];
                              return Padding(
                                padding: const EdgeInsets.symmetric(
                                  vertical: 4,
                                ),
                                child: Row(
                                  children: [
                                    Cover(
                                      url: item.thumbnailUrl,
                                      size: 36,
                                      radius: 4,
                                    ),
                                    const SizedBox(width: 10),
                                    Expanded(
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        children: [
                                          Text(
                                            item.title,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: Theme.of(context)
                                                .textTheme
                                                .bodyMedium
                                                ?.copyWith(
                                                  fontWeight: FontWeight.w600,
                                                ),
                                          ),
                                          Text(
                                            item.sourceAttribution,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: Theme.of(context)
                                                .textTheme
                                                .bodySmall
                                                ?.copyWith(fontSize: 11.5),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ],
                                ),
                              );
                            },
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
                const SizedBox(height: 14),
                FilledButton.icon(
                  key: const Key('import-shared-album-submit'),
                  onPressed: _busy ? null : _importNow,
                  icon: const Icon(Icons.download_done_rounded),
                  label: Text(context.tr('importSharedBtn')),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
