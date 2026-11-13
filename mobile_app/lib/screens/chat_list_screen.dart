import 'dart:async';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../app_theme.dart';
import '../services/api_service.dart';
import 'chat_screen.dart';

// List of everyone you can chat with (people you already talk to first).
class ChatListScreen extends StatefulWidget {
  const ChatListScreen({super.key});
  @override
  State<ChatListScreen> createState() => _ChatListScreenState();
}

class _ChatListScreenState extends State<ChatListScreen> {
  List<Map<String, dynamic>> _contacts = [];
  bool _loading = true;
  String? _error;
  String _query = '';
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _load();
    _timer = Timer.periodic(const Duration(seconds: 10), (_) => _load(silent: true));
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _load({bool silent = false}) async {
    if (!silent) setState(() { _loading = true; _error = null; });
    try {
      final c = await ApiService.getChatContacts();
      if (!mounted) return;
      setState(() { _contacts = c; _loading = false; _error = null; });
    } catch (e) {
      if (!mounted) return;
      if (!silent || _contacts.isEmpty) setState(() { _loading = false; _error = 'Chat list load nahi hui. Internet check karo.'; });
    }
  }

  String _initials(String name) {
    final p = name.trim().split(RegExp(r'\s+'));
    if (p.isEmpty || p.first.isEmpty) return '?';
    final a = p.first[0];
    final b = p.length > 1 ? p.last[0] : '';
    return (a + b).toUpperCase();
  }

  String _timeLabel(String at) {
    final d = DateTime.tryParse(at.replaceFirst(' ', 'T'));
    if (d == null) return '';
    final now = DateTime.now();
    if (d.year == now.year && d.month == now.month && d.day == now.day) return DateFormat('h:mm a').format(d);
    return DateFormat('d MMM').format(d);
  }

  @override
  Widget build(BuildContext context) {
    final q = _query.trim().toLowerCase();
    final list = _contacts.where((c) {
      if (q.isEmpty) return true;
      final s = '${c['name'] ?? ''} ${c['dept'] ?? ''} ${c['username'] ?? ''}'.toLowerCase();
      return s.contains(q);
    }).toList();

    return Scaffold(
      backgroundColor: AppColors.bg,
      appBar: AppBar(
        backgroundColor: AppColors.navy,
        iconTheme: const IconThemeData(color: Colors.white),
        title: const Text('Chat', style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white)),
      ),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 10, 12, 6),
          child: TextField(
            onChanged: (v) => setState(() => _query = v),
            decoration: InputDecoration(
              hintText: 'Naam ya department search karo',
              prefixIcon: const Icon(Icons.search_rounded),
              filled: true,
              fillColor: Colors.white,
              isDense: true,
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: BorderSide.none),
            ),
          ),
        ),
        Expanded(
          child: _loading
              ? const Center(child: CircularProgressIndicator(color: AppColors.blue))
              : _error != null
                  ? Center(child: Column(mainAxisSize: MainAxisSize.min, children: [
                      Text(_error!, style: const TextStyle(color: AppColors.textMuted)),
                      const SizedBox(height: 8),
                      ElevatedButton(onPressed: _load, child: const Text('Retry')),
                    ]))
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.separated(
                        itemCount: list.length,
                        separatorBuilder: (_, __) => const Divider(height: 1, indent: 72),
                        itemBuilder: (_, i) {
                          final c = list[i];
                          final name = (c['name'] ?? '').toString();
                          final dept = (c['dept'] ?? '').toString();
                          final last = (c['last'] ?? '').toString();
                          final unread = ((c['unread'] ?? 0) as num).toInt();
                          final mine = c['mine'] == true;
                          return ListTile(
                            tileColor: Colors.white,
                            leading: CircleAvatar(
                              backgroundColor: AppColors.navy,
                              child: Text(_initials(name), style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600)),
                            ),
                            title: Row(children: [
                              Expanded(child: Text(name, maxLines: 1, overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14))),
                              Text(_timeLabel((c['lastAt'] ?? '').toString()),
                                  style: const TextStyle(fontSize: 11, color: AppColors.textMuted)),
                            ]),
                            subtitle: Row(children: [
                              Expanded(child: Text(
                                  last.isEmpty ? dept : '${mine ? 'You: ' : ''}$last',
                                  maxLines: 1, overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(fontSize: 12.5, color: AppColors.textMuted))),
                              if (unread > 0)
                                Container(
                                  margin: const EdgeInsets.only(left: 6),
                                  padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 1),
                                  decoration: BoxDecoration(color: const Color(0xFF25D366), borderRadius: BorderRadius.circular(10)),
                                  child: Text('$unread', style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.bold)),
                                ),
                            ]),
                            onTap: () async {
                              await Navigator.push(context, MaterialPageRoute(
                                  builder: (_) => ChatScreen(peer: (c['username'] ?? '').toString(), peerName: name, peerDept: dept)));
                              _load(silent: true);
                            },
                          );
                        },
                      ),
                    ),
        ),
      ]),
    );
  }
}
