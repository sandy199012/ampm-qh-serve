import 'dart:async';
import 'package:flutter/material.dart';
import '../services/api_service.dart';
import '../screens/chat_list_screen.dart';

// AppBar chat icon with a red unread-messages count. Checks the server every
// 25 seconds while the screen is open.
class ChatIconButton extends StatefulWidget {
  const ChatIconButton({super.key});
  @override
  State<ChatIconButton> createState() => _ChatIconButtonState();
}

class _ChatIconButtonState extends State<ChatIconButton> {
  int _unread = 0;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _refresh();
    _timer = Timer.periodic(const Duration(seconds: 25), (_) => _refresh());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _refresh() async {
    try {
      final n = await ApiService.getChatUnread();
      if (mounted && n != _unread) setState(() => _unread = n);
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    return Stack(
      alignment: Alignment.center,
      children: [
        IconButton(
          tooltip: 'Chat',
          icon: const Icon(Icons.chat_bubble_outline_rounded, color: Colors.white),
          onPressed: () async {
            await Navigator.push(context, MaterialPageRoute(builder: (_) => const ChatListScreen()));
            _refresh();
          },
        ),
        if (_unread > 0)
          Positioned(
            right: 4,
            top: 6,
            child: IgnorePointer(
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                decoration: BoxDecoration(color: Colors.red, borderRadius: BorderRadius.circular(10)),
                child: Text(_unread > 99 ? '99+' : '$_unread',
                    style: const TextStyle(color: Colors.white, fontSize: 10, fontWeight: FontWeight.bold)),
              ),
            ),
          ),
      ],
    );
  }
}
