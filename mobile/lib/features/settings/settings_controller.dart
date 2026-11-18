import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/session.dart';

class UserSettings {
  const UserSettings({this.theme = 'dark', this.language = 'vi'});
  final String theme, language;
  factory UserSettings.fromJson(Map<String, dynamic> value) => UserSettings(
    theme: const ['dark', 'light', 'system'].contains(value['theme'])
        ? value['theme'] as String
        : 'dark',
    language: const ['vi', 'en'].contains(value['language'])
        ? value['language'] as String
        : 'vi',
  );
  Map<String, dynamic> toJson() => {'theme': theme, 'language': language};
  ThemeMode get mode => switch (theme) {
    'light' => ThemeMode.light,
    'system' => ThemeMode.system,
    _ => ThemeMode.dark,
  };
}

final settingsProvider =
    AsyncNotifierProvider<SettingsController, UserSettings>(
      SettingsController.new,
      retry: (_, _) => null,
    );

class SettingsController extends AsyncNotifier<UserSettings> {
  int _revision = 0;
  Future<void> _storageWrites = Future.value();
  static String _storageKey(String? userId) =>
      'tmusic.v2.settings.${userId ?? "guest"}';

  Future<UserSettings> _readLocal(String? userId) async {
    final raw = await ref
        .read(secureStorageProvider)
        .read(key: _storageKey(userId));
    if (raw != null) {
      try {
        final decoded = jsonDecode(raw);
        if (decoded is Map) {
          return UserSettings.fromJson(Map<String, dynamic>.from(decoded));
        }
      } on FormatException {
        // An invalid preference must not prevent opening the app.
      }
    }
    return const UserSettings();
  }

  Future<void> _persist(String? userId, UserSettings value, int revision) {
    final write = _storageWrites.then((_) async {
      if (revision != _revision) return;
      await ref
          .read(secureStorageProvider)
          .write(key: _storageKey(userId), value: jsonEncode(value.toJson()));
    });
    _storageWrites = write.then((_) {}, onError: (Object _, StackTrace _) {});
    return write;
  }

  @override
  Future<UserSettings> build() async {
    final revision = ++_revision;
    final userId = ref.watch(sessionProvider.select((s) => s.value?.userId));
    final local = await _readLocal(userId);
    if (revision != _revision) return state.value ?? local;
    if (userId == null) return local;
    try {
      final j = await ref
          .read(authorizedApiProvider)
          .request('GET', '/users/me/settings');
      final value = UserSettings.fromJson(j);
      if (revision != _revision) return state.value ?? local;
      await _persist(userId, value, revision);
      return revision == _revision ? value : state.value ?? local;
    } on ApiFailure catch (e) {
      if (revision != _revision) return state.value ?? local;
      if (e.status == null || (e.status ?? 0) >= 500) return local;
      rethrow;
    }
  }

  Future<void> save({String? theme, String? language}) async {
    final revision = ++_revision;
    final userId = ref.read(sessionProvider).value?.userId;
    final before = state.value ?? const UserSettings();
    final next = UserSettings(
      theme: theme ?? before.theme,
      language: language ?? before.language,
    );
    if (userId == null) {
      await _persist(null, next, revision);
      if (revision == _revision && ref.read(sessionProvider).value == null) {
        state = AsyncData(next);
      }
      return;
    }
    final j = await ref
        .read(authorizedApiProvider)
        .request(
          'PATCH',
          '/users/me/settings',
          data: {
            'theme': next.theme,
            'language': next.language,
            'notificationsEnabled': false,
          },
        );
    final saved = UserSettings.fromJson({...next.toJson(), ...j});
    await _persist(userId, saved, revision);
    if (revision == _revision &&
        ref.read(sessionProvider).value?.userId == userId) {
      state = AsyncData(saved);
    }
  }
}
