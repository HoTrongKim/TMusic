import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

class LinkDraft {
  const LinkDraft({
    this.url = '',
    this.title = '',
    this.artist = '',
    this.albumId,
  });
  final String url, title, artist;
  final String? albumId;
}

final linkDraftProvider = NotifierProvider<LinkDraftController, LinkDraft>(
  LinkDraftController.new,
);

class LinkDraftController extends Notifier<LinkDraft> {
  @override
  LinkDraft build() => const LinkDraft();
  void update({
    String? url,
    String? title,
    String? artist,
    String? albumId,
  }) => state = LinkDraft(
    url: url ?? state.url,
    title: title ?? state.title,
    artist: artist ?? state.artist,
    albumId: albumId ?? state.albumId,
  );
  void clear() => state = const LinkDraft();
}

final sharedLinksProvider = NotifierProvider<SharedLinks, List<String>>(
  SharedLinks.new,
);

class SharedLinks extends Notifier<List<String>> {
  static const channel = MethodChannel('tmusic/shared_links');
  @override
  List<String> build() => [];

  static const _supportedHosts = {
    'youtube.com',
    'www.youtube.com',
    'm.youtube.com',
    'music.youtube.com',
    'youtu.be',
    'tiktok.com',
    'www.tiktok.com',
    'm.tiktok.com',
    'vm.tiktok.com',
    'vt.tiktok.com',
    'soundcloud.com',
    'www.soundcloud.com',
    'm.soundcloud.com',
    'on.soundcloud.com',
  };

  static bool supported(String value) {
    final uri = Uri.tryParse(value.trim());
    if (uri == null ||
        uri.scheme != 'https' ||
        uri.userInfo.isEmpty == false ||
        uri.port != 443 ||
        !_supportedHosts.contains(uri.host)) {
      return false;
    }
    if (uri.host.contains('soundcloud.com') && uri.host != 'on.soundcloud.com') {
      final segs = uri.pathSegments.where((s) => s.isNotEmpty).toList();
      if (segs.length != 2) return false;
      const reservedRoots = {
        'discover',
        'search',
        'stream',
        'you',
        'settings',
        'upload',
        'charts',
        'pages',
        'oembed',
      };
      const reservedSub = {'sets', 'likes', 'reposts', 'tracks', 'albums', 'playlists'};
      if (reservedRoots.contains(segs[0].toLowerCase()) ||
          reservedSub.contains(segs[1].toLowerCase())) {
        return false;
      }
    }
    return true;
  }

  static String detectProvider(String url) {
    final host = Uri.tryParse(url.trim())?.host.toLowerCase() ?? '';
    if (host.contains('soundcloud')) return 'soundcloud';
    if (host.contains('tiktok')) return 'tiktok';
    return 'youtube';
  }

  static List<String> parse(String text) =>
      RegExp(r'https://[^\s<>"\u0000-\u001F]+', caseSensitive: false)
          .allMatches(text)
          .map((m) => m.group(0)!.replaceFirst(RegExp(r'[),.;!?]+$'), ''))
          .where((u) => u.length <= 2048 && supported(u))
          .toSet()
          .take(8)
          .toList();

  Future<bool> receive() async {
    try {
      final input = await channel.invokeListMethod<String>('takePending') ?? [];
      final links = input.expand(parse);
      final next = {...state, ...links}.take(8).toList();
      if (next.length == state.length) return false;
      state = next;
      return state.isNotEmpty;
    } on MissingPluginException {
      return false;
    } on PlatformException {
      return false;
    }
  }

  void consume(String value) => state = state.where((s) => s != value).toList();
}
