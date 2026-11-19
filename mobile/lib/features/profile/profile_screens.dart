import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/external_auth.dart';
import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/storage/app_metadata.dart';
import '../../core/storage/library_cache.dart';
import '../../core/widgets/common.dart';
import '../library/library_repository.dart';
import '../playback/local_audio.dart';
import '../settings/settings_controller.dart';

class ProfileLibraryStats {
  const ProfileLibraryStats({
    this.totalSongs,
    this.totalAlbums,
    this.favoriteSongs,
    required this.downloadedAudioBytes,
    required this.cacheBytes,
  });

  final int? totalSongs;
  final int? totalAlbums;
  final int? favoriteSongs;
  final int downloadedAudioBytes;
  final int cacheBytes;
}

final profileProvider = FutureProvider((ref) async {
  if (ref.watch(sessionProvider).value == null) return <String, dynamic>{};
  return ref.read(authorizedApiProvider).request('GET', '/users/me');
}, retry: (_, _) => null);

final profileLibraryStatsProvider = FutureProvider<ProfileLibraryStats>((
  ref,
) async {
  ref.watch(sessionProvider.select((s) => s.value?.userId));
  ref.watch(libraryRevisionProvider);
  final repo = ref.watch(libraryRepositoryProvider);
  final cache = ref.watch(libraryCacheProvider);

  int? totalSongs;
  int? totalAlbums;
  int? favoriteSongs;
  try {
    final songsPage = await repo.songs(page: 1);
    final albumsPage = await repo.albums(page: 1);
    final favPage = await repo.songs(page: 1, favorite: true);
    totalSongs = songsPage.total;
    totalAlbums = albumsPage.total;
    favoriteSongs = favPage.total;
  } catch (_) {}

  var audioBytes = 0;
  try {
    final local = await ref.read(localLibraryProvider.future);
    audioBytes = await local.storageUsageBytes();
  } catch (_) {}

  var cacheBytes = 0;
  try {
    cacheBytes = await cache.cacheSizeBytes();
  } catch (_) {}

  return ProfileLibraryStats(
    totalSongs: totalSongs,
    totalAlbums: totalAlbums,
    favoriteSongs: favoriteSongs,
    downloadedAudioBytes: audioBytes,
    cacheBytes: cacheBytes,
  );
}, retry: (_, _) => null);

/// Matchy & Cross-inspired Studio Profile Screen with Multi-Color Tinted Menu Badges.
class ProfileScreen extends ConsumerStatefulWidget {
  const ProfileScreen({super.key});
  @override
  ConsumerState<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends ConsumerState<ProfileScreen> {
  bool _busy = false;
  Object? _error;

  Future<void> _logout() async {
    setState(() => _busy = true);
    try {
      await ref.read(authorizedApiProvider).request('POST', '/auth/logout');
      await ref.read(sessionProvider.notifier).clear();
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _editProfile(Map<String, dynamic> p) async {
    final name = TextEditingController(text: p['displayName'] as String? ?? '');
    var avatar = p['avatarUrl'] as String? ?? '';
    final saved = await showDialog<(String, String)>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setModal) => AlertDialog(
          title: Text(context.tr('edit')),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                TextField(
                  controller: name,
                  maxLength: 80,
                  decoration: InputDecoration(labelText: context.tr('name')),
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final c in ['navy', 'blue', 'green', 'terracotta'])
                      ChoiceChip(
                        showCheckmark: false,
                        avatar: Cover(url: 'avatar:$c', size: 20, radius: 10),
                        label: Text(context.tr('cover_$c')),
                        selected: avatar == 'avatar:$c',
                        onSelected: (_) => setModal(() => avatar = 'avatar:$c'),
                      ),
                  ],
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: Text(context.tr('cancel')),
            ),
            FilledButton(
              onPressed: () =>
                  Navigator.pop(context, (name.text.trim(), avatar)),
              child: Text(context.tr('save')),
            ),
          ],
        ),
      ),
    );
    name.dispose();
    if (saved != null && saved.$1.isNotEmpty) {
      try {
        await ref
            .read(authorizedApiProvider)
            .request(
              'PATCH',
              '/users/me',
              data: {'displayName': saved.$1, 'avatarUrl': saved.$2},
            );
        ref.invalidate(profileProvider);
      } catch (e) {
        if (mounted) setState(() => _error = e);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final isGuest = ref.watch(sessionProvider).value == null;
    final profile = isGuest ? null : ref.watch(profileProvider);
    final statsAsync = ref.watch(profileLibraryStatsProvider);
    final stats = statsAsync.hasValue ? statsAsync.value : null;
    final appVersionAsync = ref.watch(appVersionProvider);
    final appVersion = appVersionAsync.hasValue ? appVersionAsync.value : null;
    final scheme = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(title: Text(context.tr('profile'))),
      body: PageList(
        children: [
          if (_busy) const LinearProgressIndicator(),
          if (_error != null) Notice(errorText(context, _error!), error: true),
          if (profile == null)
            const _GuestProfileCard()
          else
            profile.when(
              data: (p) {
                final providers = (p['authProviders'] as List? ?? const [])
                    .map((e) => '$e')
                    .toList();
                final avatarUrl = p['avatarUrl'] as String?;
                return Material(
                  color: scheme.surface,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(22),
                    side: BorderSide(
                      color: scheme.outlineVariant.withValues(alpha: 0.65),
                    ),
                  ),
                  child: Padding(
                    padding: const EdgeInsets.all(18),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            avatarUrl != null && avatarUrl.isNotEmpty
                                ? Cover(url: avatarUrl, size: 64, radius: 32)
                                : const TintedIconBadge(
                                    icon: Icons.person_rounded,
                                    size: 64,
                                    iconSize: 32,
                                  ),
                            const SizedBox(width: 14),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    (p['displayName'] as String? ?? '')
                                            .isNotEmpty
                                        ? p['displayName'] as String
                                        : 'TMusic',
                                    style: Theme.of(context)
                                        .textTheme
                                        .headlineSmall,
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    p['email'] as String? ?? '',
                                    style: Theme.of(context)
                                        .textTheme
                                        .bodySmall,
                                  ),
                                ],
                              ),
                            ),
                            IconButton(
                              tooltip: context.tr('edit'),
                              onPressed: () => _editProfile(p),
                              icon: const Icon(Icons.edit_outlined),
                            ),
                          ],
                        ),
                        if (providers.isNotEmpty) ...[
                          const SizedBox(height: 12),
                          Wrap(
                            spacing: 8,
                            runSpacing: 6,
                            children: providers
                                .map(
                                  (prov) => Chip(
                                    label: Text(switch (prov) {
                                      'password' => 'Email & Password',
                                      'google' => 'Google',
                                      'apple' => 'Apple',
                                      _ => prov,
                                    }),
                                    visualDensity: VisualDensity.compact,
                                  ),
                                )
                                .toList(),
                          ),
                        ],
                      ],
                    ),
                  ),
                );
              },
              error: (e, _) => Column(
                children: [
                  Notice(errorText(context, e), error: true),
                  OutlinedButton(
                    onPressed: () => ref.invalidate(profileProvider),
                    child: Text(context.tr('retry')),
                  ),
                ],
              ),
              loading: () => const LinearProgressIndicator(),
            ),
          const SizedBox(height: 12),
          // Grouped Library & Preferences Card
          Material(
            color: scheme.surface,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(20),
              side: BorderSide(
                color: scheme.outlineVariant.withValues(alpha: 0.6),
              ),
            ),
            clipBehavior: Clip.antiAlias,
            child: Column(
              children: [
                _ProfileMenuTile(
                  icon: Icons.favorite_rounded,
                  color: const Color(0xFFE05263),
                  title: context.tr('favorites'),
                  onTap: () => context.push('/favorites'),
                ),
                Divider(
                  height: 1,
                  indent: 66,
                  color: scheme.outlineVariant.withValues(alpha: 0.45),
                ),
                _ProfileMenuTile(
                  icon: Icons.headphones_rounded,
                  color: const Color(0xFF34B38F),
                  title: context.tr('offlineAudio'),
                  onTap: () => context.push('/local'),
                ),
                Divider(
                  height: 1,
                  indent: 66,
                  color: scheme.outlineVariant.withValues(alpha: 0.45),
                ),
                _ProfileMenuTile(
                  icon: Icons.tune_rounded,
                  key: const Key('profile-settings-shortcut'),
                  color: scheme.primary,
                  title: context.tr('settings'),
                  onTap: () => context.push('/settings'),
                ),
              ],
            ),
          ),
          if (stats != null &&
              (stats.totalSongs != null ||
                  stats.totalAlbums != null ||
                  stats.favoriteSongs != null)) ...[
            const SizedBox(height: 14),
            _ProfileStatsCard(stats: stats),
          ],
          const SizedBox(height: 14),
          _ProfileStorageCard(
            audioBytes: stats?.downloadedAudioBytes ?? 0,
            cacheBytes: stats?.cacheBytes ?? 0,
            busy: _busy,
            onManageAudio: () => context.push('/local'),
            onClearCache: () async {
              await ref.read(libraryCacheProvider).clearAll();
              ref.invalidate(profileLibraryStatsProvider);
              if (context.mounted) {
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(content: Text(context.tr('cacheCleared'))),
                );
              }
            },
          ),
          const SizedBox(height: 14),
          // Grouped Account & Security Card
          Material(
            color: scheme.surface,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(20),
              side: BorderSide(
                color: scheme.outlineVariant.withValues(alpha: 0.6),
              ),
            ),
            clipBehavior: Clip.antiAlias,
            child: Column(
              children: [
                if (!isGuest) ...[
                  _ProfileMenuTile(
                    icon: Icons.shield_outlined,
                    color: const Color(0xFFE5A443),
                    title: context.tr('security'),
                    onTap: () => context.push('/security'),
                  ),
                  Divider(
                    height: 1,
                    indent: 66,
                    color: scheme.outlineVariant.withValues(alpha: 0.45),
                  ),
                  _ProfileMenuTile(
                    icon: Icons.devices_rounded,
                    color: const Color(0xFF937AF0),
                    title: context.tr('sessions'),
                    onTap: () => context.push('/sessions'),
                  ),
                  Divider(
                    height: 1,
                    indent: 66,
                    color: scheme.outlineVariant.withValues(alpha: 0.45),
                  ),
                ],
                _ProfileMenuTile(
                  icon: Icons.verified_user_outlined,
                  color: const Color(0xFF38A8DF),
                  title: context.tr('privacy'),
                  onTap: () => context.push('/privacy'),
                ),
              ],
            ),
          ),
          if (!isGuest) ...[
            const SizedBox(height: 20),
            OutlinedButton.icon(
              key: const Key('profile-logout'),
              onPressed: _busy ? null : _logout,
              icon: const Icon(Icons.logout_rounded),
              label: Text(context.tr('logout')),
            ),
          ],
          const SizedBox(height: 26),
          Column(
            children: [
              Text(
                context.tr('authorCredit'),
                key: const Key('profile-author-credit'),
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  fontSize: 12,
                  color: scheme.onSurfaceVariant,
                ),
              ),
              if (appVersion != null && appVersion.displayLabel.isNotEmpty) ...[
                const SizedBox(height: 3),
                Text(
                  'TMusic ${appVersion.displayLabel}',
                  key: const Key('profile-app-version'),
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    fontSize: 11.5,
                    color: scheme.onSurfaceVariant.withValues(alpha: 0.78),
                  ),
                ),
              ],
            ],
          ),
          const SizedBox(height: 12),
        ],
      ),
    );
  }
}

class _ProfileStatsCard extends StatelessWidget {
  const _ProfileStatsCard({required this.stats});
  final ProfileLibraryStats stats;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final items = <(String, String)>[
      if (stats.totalSongs != null) (context.tr('songs'), '${stats.totalSongs}'),
      if (stats.totalAlbums != null)
        (context.tr('albums'), '${stats.totalAlbums}'),
      if (stats.favoriteSongs != null)
        (context.tr('favorites'), '${stats.favoriteSongs}'),
      (
        context.tr('downloadedAudioSize'),
        formatStorageBytes(stats.downloadedAudioBytes),
      ),
    ];

    return Material(
      key: const Key('profile-library-stats'),
      color: scheme.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(18),
        side: BorderSide(color: scheme.outlineVariant.withValues(alpha: 0.6)),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 10),
        child: LayoutBuilder(
          builder: (context, constraints) {
            final textScale = MediaQuery.textScalerOf(context).scale(1);
            final compactRow = constraints.maxWidth >= 300 && textScale <= 1.3;
            if (compactRow) {
              return Row(
                children: [
                  for (final (label, value) in items)
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 4),
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text(
                              value,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context).textTheme.titleMedium
                                  ?.copyWith(fontWeight: FontWeight.w800),
                            ),
                            const SizedBox(height: 2),
                            Text(
                              label,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context).textTheme.bodySmall
                                  ?.copyWith(fontSize: 11),
                            ),
                          ],
                        ),
                      ),
                    ),
                ],
              );
            }
            final columns = constraints.maxWidth >= 280 && textScale <= 1.6
                ? 2
                : 1;
            final width = (constraints.maxWidth - (columns - 1) * 8) / columns;
            return Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final (label, value) in items)
                  SizedBox(
                    width: width,
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: 8,
                      ),
                      decoration: BoxDecoration(
                        color: scheme.surfaceContainerLow,
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            value,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context).textTheme.titleMedium
                                ?.copyWith(fontWeight: FontWeight.w800),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            label,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                      ),
                    ),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _ProfileStorageCard extends StatelessWidget {
  const _ProfileStorageCard({
    required this.audioBytes,
    required this.cacheBytes,
    required this.busy,
    required this.onManageAudio,
    required this.onClearCache,
  });

  final int audioBytes;
  final int cacheBytes;
  final bool busy;
  final VoidCallback onManageAudio;
  final VoidCallback onClearCache;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      key: const Key('profile-storage-card'),
      color: scheme.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(20),
        side: BorderSide(color: scheme.outlineVariant.withValues(alpha: 0.6)),
      ),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              context.tr('storageUsage'),
              style: Theme.of(
                context,
              ).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 10),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: Text(
                    context.tr('downloadedAudioSize'),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                ),
                Text(
                  formatStorageBytes(audioBytes),
                  style: Theme.of(context).textTheme.labelLarge,
                ),
              ],
            ),
            const SizedBox(height: 6),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: Text(
                    context.tr('cacheSize'),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                ),
                Text(
                  formatStorageBytes(cacheBytes),
                  style: Theme.of(context).textTheme.labelLarge,
                ),
              ],
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                OutlinedButton.icon(
                  onPressed: busy ? null : onManageAudio,
                  icon: const Icon(Icons.folder_open_rounded, size: 18),
                  label: Text(context.tr('manageDownloadedAudio')),
                ),
                TextButton.icon(
                  onPressed: busy ? null : onClearCache,
                  icon: const Icon(Icons.cleaning_services_outlined, size: 18),
                  label: Text(context.tr('clearCache')),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _GuestProfileCard extends StatelessWidget {
  const _GuestProfileCard();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainer,
        borderRadius: BorderRadius.circular(20),
      ),
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const TintedIconBadge(
              icon: Icons.person_outline_rounded,
              size: 56,
              iconSize: 28,
            ),
            const SizedBox(height: 12),
            Text(
              context.tr('guestProfileTitle'),
              style: theme.textTheme.headlineSmall,
            ),
            const SizedBox(height: 6),
            Text(context.tr('guestProfileBody')),
            const SizedBox(height: 16),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                FilledButton(
                  key: const Key('profile-guest-login'),
                  onPressed: () => context.push('/auth/login'),
                  child: Text(context.tr('login')),
                ),
                OutlinedButton(
                  key: const Key('profile-guest-register'),
                  onPressed: () => context.push('/auth/register'),
                  child: Text(context.tr('register')),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _ProfileMenuTile extends StatelessWidget {
  const _ProfileMenuTile({
    super.key,
    required this.icon,
    required this.color,
    required this.title,
    required this.onTap,
  });

  final IconData icon;
  final Color color;
  final String title;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => ListTile(
    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 3),
    leading: TintedIconBadge(icon: icon, color: color, size: 40, iconSize: 20),
    title: Text(
      title,
      style: Theme.of(context).textTheme.titleMedium
          ?.copyWith(fontWeight: FontWeight.w600),
    ),
    trailing: const Icon(Icons.chevron_right_rounded, size: 20),
    onTap: onTap,
  );
}

/// Settings Screen — Segmented Pill Selectors for Theme & Language (Zero Dropdowns!)
class SettingsScreen extends ConsumerStatefulWidget {
  const SettingsScreen({super.key});
  @override
  ConsumerState<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends ConsumerState<SettingsScreen> {
  bool _busy = false;
  Object? _error;
  String? _notice;

  Future<void> _save({String? theme, String? language}) async {
    setState(() {
      _busy = true;
      _notice = null;
    });
    try {
      await ref
          .read(settingsProvider.notifier)
          .save(theme: theme, language: language);
      if (mounted) setState(() => _error = null);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final settings = ref.watch(settingsProvider);
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('settings'))),
      body: PageList(
        children: [
          if (_busy || settings.isLoading) const LinearProgressIndicator(),
          if (_error != null) Notice(errorText(context, _error!), error: true),
          if (_notice != null) Notice(_notice!),
          if (settings.hasError) ...[
            Notice(errorText(context, settings.error!), error: true),
            OutlinedButton(
              onPressed: () => ref.invalidate(settingsProvider),
              child: Text(context.tr('retry')),
            ),
          ],
          if (settings.value case final s?) ...[
            Material(
              color: scheme.surface,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(20),
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
                      context.tr('theme'),
                      style: Theme.of(context).textTheme.titleMedium
                          ?.copyWith(fontWeight: FontWeight.w700),
                    ),
                    const SizedBox(height: 12),
                    SegmentedPillSelector<String>(
                      options: [
                        (
                          value: 'dark',
                          label: context.tr('dark'),
                          icon: Icons.dark_mode_rounded,
                        ),
                        (
                          value: 'light',
                          label: context.tr('light'),
                          icon: Icons.light_mode_rounded,
                        ),
                        (
                          value: 'system',
                          label: context.tr('system'),
                          icon: null,
                        ),
                      ],
                      selected: s.theme,
                      onChanged: _busy ? null : (v) => _save(theme: v),
                    ),
                    const SizedBox(height: 22),
                    Text(
                      context.tr('language'),
                      style: Theme.of(context).textTheme.titleMedium
                          ?.copyWith(fontWeight: FontWeight.w700),
                    ),
                    const SizedBox(height: 12),
                    SegmentedPillSelector<String>(
                      options: const [
                        (value: 'vi', label: 'Tiếng Việt', icon: null),
                        (value: 'en', label: 'English', icon: null),
                      ],
                      selected: s.language,
                      onChanged: _busy ? null : (v) => _save(language: v),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 18),
            OutlinedButton.icon(
              onPressed: _busy
                  ? null
                  : () async {
                      await ref.read(libraryCacheProvider).clearAll();
                      if (context.mounted) {
                        setState(() => _notice = context.tr('cacheCleared'));
                      }
                    },
              icon: const Icon(Icons.cleaning_services_outlined),
              label: Text(context.tr('clearCache')),
            ),
          ],
        ],
      ),
    );
  }
}

class SecurityScreen extends ConsumerStatefulWidget {
  const SecurityScreen({super.key});
  @override
  ConsumerState<SecurityScreen> createState() => _SecurityScreenState();
}

class _SecurityScreenState extends ConsumerState<SecurityScreen> {
  final _current = TextEditingController(), _password = TextEditingController();
  bool _busy = false;
  Object? _error;
  String? _notice;

  @override
  void dispose() {
    _current.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() {
      _busy = true;
      _error = null;
      _notice = null;
    });
    try {
      await action();
      if (mounted) setState(() => _notice = context.tr('changed'));
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(context.tr('security'))),
    body: PageList(
      children: [
        if (_busy) const LinearProgressIndicator(),
        if (_error != null) Notice(errorText(context, _error!), error: true),
        if (_notice != null) Notice(_notice!),
        Text(
          context.tr('changePassword'),
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _current,
          obscureText: true,
          autocorrect: false,
          decoration: InputDecoration(labelText: context.tr('currentPassword')),
        ),
        const SizedBox(height: 14),
        TextField(
          controller: _password,
          obscureText: true,
          autocorrect: false,
          decoration: InputDecoration(
            labelText: context.tr('newPassword'),
            helperText: context.tr('passwordHint'),
            helperMaxLines: 5,
          ),
        ),
        const SizedBox(height: 16),
        FilledButton(
          onPressed: _busy
              ? null
              : () => _run(() async {
                  await ref
                      .read(authorizedApiProvider)
                      .request(
                        'POST',
                        '/auth/change-password',
                        data: {
                          'currentPassword': _current.text,
                          'newPassword': _password.text,
                        },
                      );
                  _password.clear();
                  _current.clear();
                  await ref.read(sessionProvider.notifier).clear();
                }),
          child: Text(context.tr('changePassword')),
        ),
        const SizedBox(height: 18),
        if (ExternalAuth.googleEnabled) ...[
          OutlinedButton.icon(
            onPressed: _busy
                ? null
                : () => _run(() async {
                    await ref
                        .read(externalAuthProvider)
                        .authenticate('google', password: _current.text);
                    ref.invalidate(profileProvider);
                  }),
            icon: const Icon(Icons.link_rounded),
            label: Text(context.tr('linkGoogle')),
          ),
          const SizedBox(height: 10),
        ],
        if (ExternalAuth.appleEnabled) ...[
          OutlinedButton.icon(
            onPressed: _busy
                ? null
                : () => _run(() async {
                    await ref
                        .read(externalAuthProvider)
                        .authenticate('apple', password: _current.text);
                    ref.invalidate(profileProvider);
                  }),
            icon: const Icon(Icons.apple),
            label: Text(context.tr('linkApple')),
          ),
          const SizedBox(height: 10),
        ],
        if (ExternalAuth.googleEnabled || ExternalAuth.appleEnabled)
          Notice(context.tr('linkProof')),
        OutlinedButton.icon(
          onPressed: _busy
              ? null
              : () async {
                  if (!await confirmAction(
                    context,
                    context.tr('logoutAll'),
                    context.tr('sessionExpired'),
                  )) {
                    return;
                  }
                  await _run(() async {
                    await ref
                        .read(authorizedApiProvider)
                        .request('POST', '/auth/logout-all');
                    await ref.read(sessionProvider.notifier).clear();
                  });
                },
          icon: const Icon(Icons.logout_rounded),
          label: Text(context.tr('logoutAll')),
        ),
        const SizedBox(height: 18),
        TextButton.icon(
          key: const Key('open-delete-account-flow'),
          style: TextButton.styleFrom(
            foregroundColor: Theme.of(context).colorScheme.error,
          ),
          onPressed: _busy ? null : () => context.push('/account/delete'),
          icon: const Icon(Icons.delete_forever_rounded),
          label: Text(context.tr('deleteAccount')),
        ),
      ],
    ),
  );
}

class DeleteAccountScreen extends ConsumerStatefulWidget {
  const DeleteAccountScreen({super.key});

  @override
  ConsumerState<DeleteAccountScreen> createState() =>
      _DeleteAccountScreenState();
}

class _DeleteAccountScreenState extends ConsumerState<DeleteAccountScreen> {
  final _password = TextEditingController();
  bool _confirmed = false;
  bool _busy = false;
  Object? _error;

  @override
  void dispose() {
    _password.dispose();
    super.dispose();
  }

  Future<void> _submitDelete(bool hasPassword) async {
    if (!_confirmed) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final cache = ref.read(libraryCacheProvider);
      await ref
          .read(authorizedApiProvider)
          .request(
            'DELETE',
            '/users/me',
            data: hasPassword
                ? {'password': _password.text}
                : <String, dynamic>{},
          );
      await cache.clearAll();
      await ref.read(sessionProvider.notifier).clear();
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final profileAsync = ref.watch(profileProvider);
    final profile = profileAsync.hasValue ? profileAsync.value : null;
    final providers = (profile?['authProviders'] as List? ?? const ['password'])
        .map((e) => '$e')
        .toList();
    final hasPassword = providers.contains('password');

    return Scaffold(
      appBar: AppBar(title: Text(context.tr('deleteAccount'))),
      body: PageList(
        children: [
          if (_busy) const LinearProgressIndicator(),
          if (_error != null) Notice(errorText(context, _error!), error: true),
          Text(
            context.tr('deleteAccountFlowTitle'),
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 12),
          Notice(context.tr('deleteAccountBody'), error: true),
          if (hasPassword) ...[
            const SizedBox(height: 16),
            TextField(
              key: const Key('delete-account-password'),
              controller: _password,
              obscureText: true,
              autocorrect: false,
              decoration: InputDecoration(
                labelText: context.tr('currentPassword'),
              ),
            ),
          ],
          const SizedBox(height: 12),
          CheckboxListTile(
            key: const Key('delete-account-confirm'),
            contentPadding: EdgeInsets.zero,
            value: _confirmed,
            onChanged: (v) => setState(() => _confirmed = v ?? false),
            title: Text(context.tr('deleteAccountConfirmCheck')),
          ),
          const SizedBox(height: 20),
          FilledButton.icon(
            key: const Key('delete-account-submit'),
            style: FilledButton.styleFrom(
              backgroundColor: Theme.of(context).colorScheme.error,
              foregroundColor: Theme.of(context).colorScheme.onError,
            ),
            onPressed: _busy || !_confirmed
                ? null
                : () => _submitDelete(hasPassword),
            icon: const Icon(Icons.delete_forever_rounded),
            label: Text(context.tr('deleteAccount')),
          ),
        ],
      ),
    );
  }
}

class SessionsScreen extends ConsumerStatefulWidget {
  const SessionsScreen({super.key});
  @override
  ConsumerState<SessionsScreen> createState() => _SessionsScreenState();
}

class _SessionsScreenState extends ConsumerState<SessionsScreen> {
  late Future<Map<String, dynamic>> _sessions;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _sessions = _load();
  }

  Future<Map<String, dynamic>> _load() =>
      ref.read(authorizedApiProvider).request('GET', '/auth/sessions');

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('sessions'))),
      body: FutureBuilder<Map<String, dynamic>>(
        future: _sessions,
        builder: (context, snapshot) {
          if (snapshot.hasError) {
            return LoadFailure(
              error: snapshot.error!,
              retry: () {
                final next = _load();
                setState(() {
                  _sessions = next;
                });
              },
            );
          }
          if (!snapshot.hasData) {
            return const Center(child: CircularProgressIndicator());
          }
          return PageList(
            children: [
              if (_error != null)
                Notice(errorText(context, _error!), error: true),
              ...(snapshot.data!['items'] as List).map((value) {
                final s = Map<String, dynamic>.from(value as Map);
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
                    child: ListTile(
                      leading: TintedIconBadge(
                        icon: s['isCurrent'] == true
                            ? Icons.smartphone_rounded
                            : Icons.devices_other_rounded,
                        size: 40,
                        iconSize: 20,
                      ),
                      title: Text(s['deviceName'] as String? ?? 'TMusic'),
                      subtitle: Text(
                        s['isCurrent'] == true
                            ? context.tr('thisDevice')
                            : (s['createdAt'] as String).split('T').first,
                      ),
                      trailing: IconButton(
                        tooltip: context.tr('logout'),
                        icon: const Icon(Icons.logout_rounded),
                        onPressed: () async {
                          if (!await confirmAction(
                            context,
                            context.tr('logout'),
                            s['deviceName'] as String? ?? 'TMusic',
                          )) {
                            return;
                          }
                          try {
                            await ref
                                .read(authorizedApiProvider)
                                .request('DELETE', '/auth/sessions/${s['id']}');
                            if (s['isCurrent'] == true) {
                              await ref.read(sessionProvider.notifier).clear();
                            } else if (mounted) {
                              final next = _load();
                              setState(() {
                                _sessions = next;
                              });
                            }
                          } catch (e) {
                            if (mounted) setState(() => _error = e);
                          }
                        },
                      ),
                    ),
                  ),
                );
              }),
            ],
          );
        },
      ),
    );
  }
}

class PrivacyScreen extends StatelessWidget {
  const PrivacyScreen({super.key});
  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('privacy'))),
      body: PageList(
        children: [
          Material(
            color: scheme.surface,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(20),
              side: BorderSide(
                color: scheme.outlineVariant.withValues(alpha: 0.6),
              ),
            ),
            child: Padding(
              padding: const EdgeInsets.all(18),
              child: Text(
                context.tr('privacyBody'),
                style: Theme.of(context).textTheme.bodyLarge,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
