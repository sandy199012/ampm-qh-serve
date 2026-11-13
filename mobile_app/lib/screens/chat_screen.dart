import 'dart:async';
import 'dart:io';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:open_filex/open_filex.dart';
import '../app_theme.dart';
import '../services/api_service.dart';

// One-to-one conversation (WhatsApp style). Only you and the other person can see it.
// Messages stay for ever; files are deleted by the server 2 days after sending.
class ChatScreen extends StatefulWidget {
  final String peer;      // the other person's username
  final String peerName;
  final String peerDept;
  const ChatScreen({super.key, required this.peer, required this.peerName, this.peerDept = ''});
  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> with WidgetsBindingObserver {
  static const int _maxFileBytes = 25 * 1024 * 1024;

  final List<Map<String, dynamic>> _msgs = [];
  final TextEditingController _ctrl = TextEditingController();
  final ScrollController _scroll = ScrollController();
  Timer? _timer;
  int _lastId = 0;
  bool _loading = true;
  bool _sending = false;
  bool _polling = false;
  bool _foreground = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _poll();
    _timer = Timer.periodic(const Duration(seconds: 4), (_) {
      if (_foreground) _poll();
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    _ctrl.dispose();
    _scroll.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    _foreground = state == AppLifecycleState.resumed;
    if (_foreground) _poll();
  }

  void _snack(String m) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(m)));
  }

  void _jumpToEnd() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) _scroll.jumpTo(_scroll.position.maxScrollExtent);
    });
  }

  Future<void> _poll() async {
    if (_polling) return;
    _polling = true;
    try {
      final list = await ApiService.getChatMessages(widget.peer, after: _lastId);
      if (!mounted) return;
      final nearBottom = _loading ||
          !_scroll.hasClients ||
          (_scroll.position.maxScrollExtent - _scroll.position.pixels) < 160;
      for (final m in list) {
        final id = ((m['id'] ?? 0) as num).toInt();
        if (id > _lastId) _lastId = id;
      }
      setState(() {
        _msgs.addAll(list);
        _loading = false;
        _error = null;
      });
      if (list.isNotEmpty && nearBottom) _jumpToEnd();
    } catch (e) {
      if (mounted && _loading) {
        setState(() {
          _loading = false;
          _error = 'Chat load nahi hui. Internet check karo.';
        });
      }
    } finally {
      _polling = false;
    }
  }

  Future<void> _send() async {
    final t = _ctrl.text.trim();
    if (t.isEmpty || _sending) return;
    setState(() => _sending = true);
    try {
      await ApiService.sendChatMessage(widget.peer, t);
      _ctrl.clear();
      await _poll();
      _jumpToEnd();
    } catch (e) {
      _snack(e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _attach() async {
    if (_sending) return;
    try {
      final res = await FilePicker.platform.pickFiles();
      if (res == null || res.files.isEmpty) return;
      final f = res.files.first;
      final path = f.path;
      if (path == null) return;
      final size = await File(path).length();
      if (size > _maxFileBytes) {
        _snack('File bahut badi hai (max 25 MB).');
        return;
      }
      setState(() => _sending = true);
      await ApiService.sendChatFile(widget.peer, path, f.name, text: _ctrl.text.trim());
      _ctrl.clear();
      await _poll();
      _jumpToEnd();
    } catch (e) {
      _snack(e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _openFile(Map<String, dynamic> m) async {
    if (m['fileExpired'] == true) {
      _snack('File auto-delete ho chuki hai (2 din se purani).');
      return;
    }
    _snack('Download ho rahi hai...');
    try {
      final path = await ApiService.downloadChatFile(m['fileId'].toString(), m['fileName'].toString());
      final r = await OpenFilex.open(path);
      if (r.type != ResultType.done) {
        _snack('File save ho gayi, par kholne ke liye koi app nahi mili.');
      }
    } catch (e) {
      _snack(e.toString().replaceFirst('Exception: ', ''));
    }
  }

  DateTime? _dt(Map<String, dynamic> m) => DateTime.tryParse((m['at'] ?? '').toString().replaceFirst(' ', 'T'));

  String _dayLabel(DateTime d) {
    final now = DateTime.now();
    final t = DateTime(now.year, now.month, now.day);
    final x = DateTime(d.year, d.month, d.day);
    final diff = t.difference(x).inDays;
    if (diff == 0) return 'Today';
    if (diff == 1) return 'Yesterday';
    return DateFormat('d MMM yyyy').format(d);
  }

  String _size(num b) {
    if (b < 1024) return '${b.toInt()} B';
    if (b < 1024 * 1024) return '${(b / 1024).toStringAsFixed(0)} KB';
    return '${(b / (1024 * 1024)).toStringAsFixed(1)} MB';
  }

  Widget _bubble(Map<String, dynamic> m, double maxW) {
    final mine = m['mine'] == true;
    final text = (m['text'] ?? '').toString();
    final fileName = (m['fileName'] ?? '').toString();
    final hasFile = fileName.isNotEmpty || (m['fileId'] ?? '').toString().isNotEmpty;
    final expired = m['fileExpired'] == true;
    final d = _dt(m);

    final children = <Widget>[];
    if (hasFile) {
      children.add(InkWell(
        onTap: () => _openFile(m),
        child: Container(
          margin: const EdgeInsets.only(bottom: 4),
          padding: const EdgeInsets.all(8),
          decoration: BoxDecoration(color: Colors.black.withOpacity(0.05), borderRadius: BorderRadius.circular(6)),
          child: Row(mainAxisSize: MainAxisSize.min, children: [
            Icon(expired ? Icons.insert_drive_file_outlined : Icons.insert_drive_file_rounded,
                size: 28, color: expired ? Colors.grey : AppColors.blue),
            const SizedBox(width: 8),
            Flexible(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(fileName.isEmpty ? 'File' : fileName,
                    maxLines: 2, overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
                Text(expired ? 'File auto-delete ho chuki hai (2 din)' : '${_size((m['fileSize'] ?? 0) as num)}  ·  tap = download',
                    style: TextStyle(fontSize: 11, color: expired ? Colors.orange.shade800 : AppColors.textMuted)),
              ]),
            ),
            if (!expired) const Padding(padding: EdgeInsets.only(left: 6), child: Icon(Icons.download_rounded, size: 20)),
          ]),
        ),
      ));
    }
    if (text.isNotEmpty) children.add(Text(text, style: const TextStyle(fontSize: 14.5)));
    children.add(Padding(
      padding: const EdgeInsets.only(top: 2),
      child: Text(d == null ? '' : DateFormat('h:mm a').format(d),
          style: const TextStyle(fontSize: 10.5, color: AppColors.textMuted)),
    ));

    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        constraints: BoxConstraints(maxWidth: maxW),
        margin: const EdgeInsets.symmetric(vertical: 2, horizontal: 8),
        padding: const EdgeInsets.fromLTRB(10, 6, 10, 4),
        decoration: BoxDecoration(
          color: mine ? const Color(0xFFD9FDD3) : Colors.white,
          borderRadius: BorderRadius.circular(10),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.end, mainAxisSize: MainAxisSize.min, children: children),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final maxW = MediaQuery.of(context).size.width * 0.78;
    return Scaffold(
      backgroundColor: const Color(0xFFEFEAE2),
      appBar: AppBar(
        backgroundColor: AppColors.navy,
        iconTheme: const IconThemeData(color: Colors.white),
        title: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(widget.peerName, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.bold, color: Colors.white)),
          if (widget.peerDept.isNotEmpty)
            Text(widget.peerDept, style: const TextStyle(fontSize: 11, color: AppColors.cyan)),
        ]),
      ),
      body: Column(children: [
        Expanded(
          child: _loading
              ? const Center(child: CircularProgressIndicator(color: AppColors.blue))
              : _error != null
                  ? Center(child: Text(_error!, style: const TextStyle(color: AppColors.textMuted)))
                  : _msgs.isEmpty
                      ? const Center(child: Text('Abhi koi message nahi. Pehla message bhejo.',
                          style: TextStyle(color: AppColors.textMuted)))
                      : ListView.builder(
                          controller: _scroll,
                          padding: const EdgeInsets.symmetric(vertical: 8),
                          itemCount: _msgs.length,
                          itemBuilder: (_, i) {
                            final m = _msgs[i];
                            final d = _dt(m);
                            final prev = i > 0 ? _dt(_msgs[i - 1]) : null;
                            final newDay = d != null && (prev == null || prev.year != d.year || prev.month != d.month || prev.day != d.day);
                            return Column(children: [
                              if (newDay)
                                Container(
                                  margin: const EdgeInsets.symmetric(vertical: 8),
                                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                                  decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(8)),
                                  child: Text(_dayLabel(d!), style: const TextStyle(fontSize: 12, color: AppColors.textMuted)),
                                ),
                              _bubble(m, maxW),
                            ]);
                          },
                        ),
        ),
        if (_sending) const LinearProgressIndicator(minHeight: 2),
        SafeArea(
          top: false,
          child: Container(
            color: const Color(0xFFF0F2F5),
            padding: const EdgeInsets.fromLTRB(6, 6, 6, 6),
            child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
              IconButton(
                tooltip: 'File bhejo (max 25 MB)',
                icon: const Icon(Icons.attach_file_rounded, color: AppColors.textMuted),
                onPressed: _sending ? null : _attach,
              ),
              Expanded(
                child: TextField(
                  controller: _ctrl,
                  minLines: 1,
                  maxLines: 5,
                  textCapitalization: TextCapitalization.sentences,
                  decoration: InputDecoration(
                    hintText: 'Message likho...',
                    filled: true,
                    fillColor: Colors.white,
                    isDense: true,
                    contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(22), borderSide: BorderSide.none),
                  ),
                ),
              ),
              const SizedBox(width: 4),
              CircleAvatar(
                backgroundColor: const Color(0xFF00A884),
                child: IconButton(
                  icon: const Icon(Icons.send_rounded, color: Colors.white, size: 20),
                  onPressed: _sending ? null : _send,
                ),
              ),
            ]),
          ),
        ),
      ]),
    );
  }
}
