import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path_provider/path_provider.dart';
import 'package:sqflite/sqflite.dart';

import '../auth/session.dart';

final libraryCacheProvider = Provider<LibraryCache>((ref) {
  final userId = ref.watch(sessionProvider).value?.userId;
  return LibraryCache(userId);
});

/// Per-user SQLite read-through snapshot cache and recently-opened-albums tracker.
/// Server remains the single source of truth; cached entries are read-only fallbacks.
class LibraryCache {
  LibraryCache(this.userId, {this.enableInFlutterTest = false});
  final String? userId;
  final bool enableInFlutterTest;
  Database? _db;
  bool _unavailable = false;

  Future<Database?> _open() async {
    if (!enableInFlutterTest &&
        Platform.environment.containsKey('FLUTTER_TEST')) {
      return null;
    }
    final owner = userId;
    if (owner == null || owner.isEmpty || _unavailable) return null;
    if (_db != null) return _db;
    try {
      final root = await getApplicationSupportDirectory();
      final safe = owner.replaceAll(RegExp('[^a-zA-Z0-9_-]'), '');
      final dir = await Directory('${root.path}/cache').create(recursive: true);
      _db = await openDatabase(
        '${dir.path}/library_$safe.db',
        version: 1,
        onCreate: (db, _) async {
          await db.execute(
            'CREATE TABLE snapshots (cache_key TEXT PRIMARY KEY, payload TEXT NOT NULL, updated_at INTEGER NOT NULL)',
          );
          await db.execute(
            'CREATE TABLE recent_albums (album_id TEXT PRIMARY KEY, opened_at INTEGER NOT NULL)',
          );
        },
      );
      return _db;
    } on MissingPluginException {
      _unavailable = true;
      return null;
    } on PlatformException {
      _unavailable = true;
      return null;
    } catch (_) {
      _unavailable = true;
      return null;
    }
  }

  Future<void> writeSnapshot(String key, Map<String, dynamic> payload) async {
    final db = await _open();
    if (db == null) return;
    await db.insert('snapshots', {
      'cache_key': key,
      'payload': jsonEncode(payload),
      'updated_at': DateTime.now().toUtc().millisecondsSinceEpoch,
    }, conflictAlgorithm: ConflictAlgorithm.replace);
  }

  Future<Map<String, dynamic>?> readSnapshot(String key) async {
    final db = await _open();
    if (db == null) return null;
    final rows = await db.query(
      'snapshots',
      where: 'cache_key = ?',
      whereArgs: [key],
      limit: 1,
    );
    if (rows.isEmpty) return null;
    try {
      return jsonDecode(rows.first['payload'] as String)
          as Map<String, dynamic>;
    } catch (_) {
      return null;
    }
  }

  Future<void> recordAlbumOpened(String albumId) async {
    final db = await _open();
    if (db == null) return;
    await db.insert('recent_albums', {
      'album_id': albumId,
      'opened_at': DateTime.now().toUtc().millisecondsSinceEpoch,
    }, conflictAlgorithm: ConflictAlgorithm.replace);
  }

  Future<List<String>> recentAlbumIds({int limit = 6}) async {
    final db = await _open();
    if (db == null) return const [];
    final rows = await db.query(
      'recent_albums',
      orderBy: 'opened_at DESC',
      limit: limit,
    );
    return rows.map((r) => r['album_id'] as String).toList();
  }

  Future<void> writeLastUsedAlbumId(String? albumId) async {
    await writeSnapshot('pref:last_used_album', {'albumId': albumId});
  }

  Future<String?> readLastUsedAlbumId() async {
    final snap = await readSnapshot('pref:last_used_album');
    final val = snap?['albumId'];
    return val is String && val.isNotEmpty ? val : null;
  }

  Future<Map<String, dynamic>?> findCachedSong(String songId) async {
    final direct = await readSnapshot('song:$songId');
    if (direct != null) return direct;
    return _scanSnapshotItems('songs:%', songId);
  }

  Future<Map<String, dynamic>?> findCachedAlbum(String albumId) async {
    final direct = await readSnapshot('album:$albumId');
    if (direct != null) return direct;
    return _scanSnapshotItems('albums:%', albumId);
  }

  Future<Map<String, dynamic>?> _scanSnapshotItems(
    String keyLike,
    String targetId,
  ) async {
    final db = await _open();
    if (db == null) return null;
    final rows = await db.query(
      'snapshots',
      where: 'cache_key LIKE ?',
      whereArgs: [keyLike],
      orderBy: 'updated_at DESC',
      limit: 12,
    );
    for (final row in rows) {
      try {
        final decoded = jsonDecode(row['payload'] as String);
        if (decoded is Map && decoded['items'] is List) {
          for (final item in decoded['items'] as List) {
            if (item is Map && item['id'] == targetId) {
              return Map<String, dynamic>.from(item);
            }
          }
        }
      } catch (_) {}
    }
    return null;
  }

  Future<void> clearAll() async {
    final owner = userId;
    if (_db != null) {
      await _db!.close();
      _db = null;
    }
    if (owner == null || owner.isEmpty) return;
    try {
      final root = await getApplicationSupportDirectory();
      final safe = owner.replaceAll(RegExp('[^a-zA-Z0-9_-]'), '');
      final file = File('${root.path}/cache/library_$safe.db');
      if (await file.exists()) await file.delete();
    } catch (_) {}
  }
}
