import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

class AppVersionInfo {
  const AppVersionInfo({required this.version, required this.buildNumber});
  final String version;
  final String buildNumber;

  String get displayLabel {
    if (version.isEmpty) return '';
    return buildNumber.isNotEmpty ? 'v$version ($buildNumber)' : 'v$version';
  }
}

const _appInfoChannel = MethodChannel('tmusic/app_info');

final appVersionProvider = FutureProvider<AppVersionInfo>((ref) async {
  try {
    final map = await _appInfoChannel.invokeMapMethod<String, dynamic>(
      'getAppInfo',
    );
    return AppVersionInfo(
      version: (map?['version'] as String? ?? '').trim(),
      buildNumber: (map?['buildNumber'] as String? ?? '').trim(),
    );
  } on MissingPluginException {
    return const AppVersionInfo(version: '', buildNumber: '');
  } on PlatformException {
    return const AppVersionInfo(version: '', buildNumber: '');
  }
}, retry: (_, _) => null);

/// Normalizes text for case- and Vietnamese diacritic-insensitive search.
String normalizeVietnamese(String input) {
  final lower = input.trim().toLowerCase();
  if (lower.isEmpty) return '';
  final buffer = StringBuffer();
  for (var i = 0; i < lower.length; i++) {
    final ch = lower[i];
    buffer.write(_viFoldMap[ch] ?? ch);
  }
  return buffer.toString();
}

const Map<String, String> _viFoldMap = {
  'à': 'a', 'á': 'a', 'ạ': 'a', 'ả': 'a', 'ã': 'a',
  'â': 'a', 'ầ': 'a', 'ấ': 'a', 'ậ': 'a', 'ẩ': 'a', 'ẫ': 'a',
  'ă': 'a', 'ằ': 'a', 'ắ': 'a', 'ặ': 'a', 'ẳ': 'a', 'ẵ': 'a',
  'è': 'e', 'é': 'e', 'ẹ': 'e', 'ẻ': 'e', 'ẽ': 'e',
  'ê': 'e', 'ề': 'e', 'ế': 'e', 'ệ': 'e', 'ể': 'e', 'ễ': 'e',
  'ì': 'i', 'í': 'i', 'ị': 'i', 'ỉ': 'i', 'ĩ': 'i',
  'ò': 'o', 'ó': 'o', 'ọ': 'o', 'ỏ': 'o', 'õ': 'o',
  'ô': 'o', 'ồ': 'o', 'ố': 'o', 'ộ': 'o', 'ổ': 'o', 'ỗ': 'o',
  'ơ': 'o', 'ờ': 'o', 'ớ': 'o', 'ợ': 'o', 'ở': 'o', 'ỡ': 'o',
  'ù': 'u', 'ú': 'u', 'ụ': 'u', 'ủ': 'u', 'ũ': 'u',
  'ư': 'u', 'ừ': 'u', 'ứ': 'u', 'ự': 'u', 'ử': 'u', 'ữ': 'u',
  'ỳ': 'y', 'ý': 'y', 'ỵ': 'y', 'ỷ': 'y', 'ỹ': 'y',
  'đ': 'd',
};

bool matchesNormalizedQuery(String haystack, String query) {
  final q = normalizeVietnamese(query);
  if (q.isEmpty) return true;
  return normalizeVietnamese(haystack).contains(q);
}

String formatStorageBytes(int bytes) {
  if (bytes <= 0) return '0 B';
  if (bytes < 1024) return '$bytes B';
  if (bytes < 1024 * 1024) {
    return '${(bytes / 1024).toStringAsFixed(1)} KB';
  }
  if (bytes < 1024 * 1024 * 1024) {
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
  return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
}
