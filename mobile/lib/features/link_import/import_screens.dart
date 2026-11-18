import 'dart:async';
import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/session.dart';
import '../../core/localization/app_copy.dart';
import '../../core/widgets/common.dart';
import '../library/library_repository.dart';

class ImportsScreen extends ConsumerStatefulWidget {
  const ImportsScreen({super.key});
  @override
  ConsumerState<ImportsScreen> createState() => _ImportsScreenState();
}

class _ImportsScreenState extends ConsumerState<ImportsScreen> {
  final _url = TextEditingController();
  bool _busy = false;
  Object? _error;
  late Future<Map<String, dynamic>> _history;
  @override
  void initState() {
    super.initState();
    _history = _load();
  }

  Future<Map<String, dynamic>> _load() =>
      ref.read(authorizedApiProvider).request('GET', '/imports');
  @override
  void dispose() {
    _url.dispose();
    super.dispose();
  }

  Future<void> _preview() async {
    setState(() => _busy = true);
    try {
      final result = await ref
          .read(authorizedApiProvider)
          .request(
            'POST',
            '/imports/previews',
            data: {'url': _url.text.trim()},
          );
      if (mounted) context.push('/imports/previews/${result['previewId']}');
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(context.tr('importPlaylist'))),
    body: PageList(
      children: [
        Notice(context.tr('playlistNotice')),
        TextField(
          controller: _url,
          keyboardType: TextInputType.url,
          decoration: InputDecoration(labelText: context.tr('url')),
        ),
        const SizedBox(height: 16),
        FilledButton(
          onPressed: _busy ? null : _preview,
          child: Text(context.tr('preview')),
        ),
        if (_busy) const LinearProgressIndicator(),
        if (_error != null) Notice(errorText(context, _error!), error: true),
        const Divider(),
        FutureBuilder(
          future: _history,
          builder: (context, snapshot) {
            if (snapshot.hasError) {
              return Column(
                children: [
                  Notice(errorText(context, snapshot.error!), error: true),
                  TextButton(
                    onPressed: () {
                      final next = _load();
                      setState(() {
                        _history = next;
                      });
                    },
                    child: Text(context.tr('retry')),
                  ),
                ],
              );
            }
            if (!snapshot.hasData) return const LinearProgressIndicator();
            return Column(
              children: [
                ...(snapshot.data!['items'] as List? ?? []).map((v) {
                  final j = Map<String, dynamic>.from(v as Map);
                  return ListTile(
                    title: Text(
                      j['albumName'] as String? ?? context.tr('importPlaylist'),
                    ),
                    subtitle: Text(
                      _statusLabel(
                        context,
                        j['status'] as String? ?? 'Pending',
                      ),
                    ),
                    onTap: () =>
                        context.push('/imports/jobs/${j['jobId'] ?? j['id']}'),
                  );
                }),
              ],
            );
          },
        ),
      ],
    ),
  );
}

String _statusLabel(BuildContext context, String status) =>
    switch (status.toLowerCase()) {
      'pending' || 'queued' => 'Đang chờ',
      'fetching' || 'processing' || 'running' => 'Đang xử lý',
      'ready' => 'Sẵn sàng',
      'completed' => 'Đã hoàn tất',
      'failed' => 'Chưa hoàn tất',
      'cancelled' || 'canceled' => 'Đã hủy',
      _ => status,
    };

class ImportStatusScreen extends ConsumerStatefulWidget {
  const ImportStatusScreen({
    super.key,
    required this.id,
    required this.preview,
  });
  final String id;
  final bool preview;
  @override
  ConsumerState<ImportStatusScreen> createState() => _ImportStatusScreenState();
}

class _ImportStatusScreenState extends ConsumerState<ImportStatusScreen> {
  Timer? _timer;
  Map<String, dynamic>? _data;
  Object? _error;
  bool _busy = false;
  final _name = TextEditingController();
  late final _key = List.generate(
    24,
    (_) => Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
  ).join();
  @override
  void initState() {
    super.initState();
    _poll();
  }

  @override
  void dispose() {
    _timer?.cancel();
    _name.dispose();
    super.dispose();
  }

  String get _path => widget.preview
      ? '/imports/previews/${widget.id}'
      : '/imports/${widget.id}';
  Future<void> _poll() async {
    try {
      final value = await ref.read(authorizedApiProvider).request('GET', _path);
      if (!mounted) return;
      setState(() {
        _data = value;
        _error = null;
      });
      final status = (value['status'] as String? ?? '').toLowerCase();
      if (![
        'ready',
        'completed',
        'failed',
        'cancelled',
        'canceled',
      ].contains(status)) {
        _timer = Timer(const Duration(seconds: 3), _poll);
      } else if (status == 'completed') {
        ref.read(libraryRevisionProvider.notifier).changed();
      }
    } catch (e) {
      if (mounted) setState(() => _error = e);
    }
  }

  Future<void> _start() async {
    if (_name.text.trim().isEmpty) return;
    setState(() => _busy = true);
    try {
      final value = await ref
          .read(authorizedApiProvider)
          .request(
            'POST',
            '/imports',
            data: {'previewId': widget.id, 'albumName': _name.text.trim()},
            headers: {'Idempotency-Key': _key},
          );
      if (mounted) context.replace('/imports/jobs/${value['jobId']}');
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _action(String action) async {
    setState(() => _busy = true);
    try {
      await ref.read(authorizedApiProvider).request('POST', '$_path/$action');
      await _poll();
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final d = _data;
    final status = (d?['status'] as String? ?? '').toLowerCase();
    return Scaffold(
      appBar: AppBar(
        title: Text(context.tr(widget.preview ? 'preview' : 'importPlaylist')),
      ),
      body: PageList(
        children: [
          if (d == null && _error == null) const LinearProgressIndicator(),
          if (_error != null) ...[
            Notice(errorText(context, _error!), error: true),
            OutlinedButton(onPressed: _poll, child: Text(context.tr('retry'))),
          ],
          if (d != null) ...[
            Text(
              _statusLabel(context, d['status'] as String? ?? ''),
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            if (![
              'ready',
              'completed',
              'failed',
              'cancelled',
              'canceled',
            ].contains(status))
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 16),
                child: LinearProgressIndicator(),
              ),
            if (d['errorCode'] != null)
              Notice('Không thể tiếp tục (${d['errorCode']}).', error: true),
            const SizedBox(height: 16),
            Text('Hợp lệ: ${d['accepted'] ?? d['added'] ?? 0}'),
            Text('Trùng: ${d['duplicates'] ?? 0}'),
            Text('Không khả dụng: ${d['unavailable'] ?? d['skipped'] ?? 0}'),
            if (widget.preview && status == 'ready') ...[
              const Divider(),
              TextField(
                controller: _name,
                maxLength: 120,
                decoration: InputDecoration(labelText: context.tr('albumName')),
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: _busy ? null : _start,
                child: Text(context.tr('save')),
              ),
            ],
            if (!widget.preview &&
                ![
                  'completed',
                  'failed',
                  'cancelled',
                  'canceled',
                ].contains(status))
              OutlinedButton(
                onPressed: _busy ? null : () => _action('cancel'),
                child: Text(context.tr('cancel')),
              ),
            if (!widget.preview && status == 'failed')
              OutlinedButton(
                onPressed: _busy ? null : () => _action('retry'),
                child: Text(context.tr('retry')),
              ),
            if (status == 'completed')
              FilledButton(
                onPressed: () => context.go('/library'),
                child: Text(context.tr('viewLibrary')),
              ),
            const Divider(),
            ...(d['items'] as List? ?? []).take(50).map((v) {
              final j = Map<String, dynamic>.from(v as Map);
              return ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(
                  j['title'] as String? ?? j['videoId'] as String? ?? '',
                ),
                subtitle: Text(j['status'] as String? ?? ''),
              );
            }),
          ],
        ],
      ),
    );
  }
}
