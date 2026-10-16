import 'dart:async';
import 'package:flutter/material.dart';
import '../models/ticket.dart';
import '../services/api_service.dart';
import 'ticket_detail_screen.dart';

class TicketListScreen extends StatefulWidget {
  final String? filter;
  final bool all;
  const TicketListScreen({super.key, this.filter, this.all = false});
  @override
  State<TicketListScreen> createState() => _TicketListScreenState();
}

class _TicketListScreenState extends State<TicketListScreen> {
  List<Ticket> _tickets = [];
  List<Ticket> _filtered = [];
  bool _loading = true;
  final _search = TextEditingController();
  String _status = 'All';

  Timer? _autoRefresh;

  @override
  void initState() {
    super.initState();
    if (widget.filter != null) _status = widget.filter!;
    _load();
    _autoRefresh = Timer.periodic(const Duration(seconds: 20), (_) => _load(silent: true));
  }

  @override
  void dispose() { _autoRefresh?.cancel(); super.dispose(); }

  Future<void> _load({bool silent = false}) async {
    if (!silent) setState(() => _loading = true);
    try {
      _tickets = widget.all ? await ApiService.getAllTickets() : await ApiService.getMyTickets();
      _applyFilter();
    } catch (e) {
      if (!silent) setState(() => _loading = false);
    }
  }

  void _applyFilter() {
    var list = _tickets.toList();
    if (_status != 'All') list = list.where((t) => t.status == _status).toList();
    if (_search.text.isNotEmpty) {
      final q = _search.text.toLowerCase();
      list = list.where((t) =>
        t.ticketNo.toLowerCase().contains(q) ||
        t.empName.toLowerCase().contains(q) ||
        t.title.toLowerCase().contains(q) ||
        t.empDept.toLowerCase().contains(q)).toList();
    }
    setState(() { _filtered = list; _loading = false; });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: Text(_status == 'All' ? 'All Tickets' : '$_status Tickets'),
        backgroundColor: const Color(0xFF0A192F),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        // Search + Filter
        Container(
          color: Colors.white,
          padding: const EdgeInsets.all(12),
          child: Column(children: [
            TextField(
              controller: _search,
              onChanged: (_) => _applyFilter(),
              decoration: InputDecoration(
                hintText: 'Search ticket, employee...',
                prefixIcon: const Icon(Icons.search, color: Colors.grey),
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
                contentPadding: const EdgeInsets.symmetric(vertical: 8),
                isDense: true,
              ),
            ),
            const SizedBox(height: 8),
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(children: ['All','Open','In Progress','Resolved','Closed'].map((s) =>
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: FilterChip(
                    label: Text(s, style: TextStyle(
                      color: _status == s ? Colors.white : const Color(0xFF374151),
                      fontWeight: FontWeight.w600, fontSize: 12)),
                    selected: _status == s,
                    onSelected: (_) { setState(() => _status = s); _applyFilter(); },
                    selectedColor: const Color(0xFF2563EB),
                    backgroundColor: const Color(0xFFF3F4F6),
                    side: BorderSide.none,
                  ),
                )
              ).toList()),
            ),
          ]),
        ),

        // Count
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: Row(children: [
            Text('${_filtered.length} tickets', style: const TextStyle(color: Colors.grey, fontSize: 12)),
          ]),
        ),

        // List
        Expanded(
          child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _filtered.isEmpty
              ? const Center(child: Text('No tickets found', style: TextStyle(color: Colors.grey)))
              : ListView.builder(
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  itemCount: _filtered.length,
                  itemBuilder: (_, i) => _card(_filtered[i]),
                ),
        ),
      ]),
    );
  }

  Widget _card(Ticket t) {
    return GestureDetector(
      onTap: () => Navigator.push(context,
          MaterialPageRoute(builder: (_) => TicketDetailScreen(ticket: t))).then((_) => _load()),
      child: Container(
        margin: const EdgeInsets.only(bottom: 10),
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: t.statusColor, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)],
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Text(t.ticketNo, style: const TextStyle(fontWeight: FontWeight.bold, color: Color(0xFF2563EB), fontSize: 12)),
            const SizedBox(width: 8),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: t.statusColor.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text(t.status, style: TextStyle(color: t.statusColor, fontSize: 10, fontWeight: FontWeight.bold))),
            const Spacer(),
            Container(padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
              decoration: BoxDecoration(color: t.priorityColor.withOpacity(.1), borderRadius: BorderRadius.circular(8)),
              child: Text(t.priority, style: TextStyle(color: t.priorityColor, fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          const SizedBox(height: 6),
          Text(t.title, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
              maxLines: 2, overflow: TextOverflow.ellipsis),
          const SizedBox(height: 4),
          Row(children: [
            const Icon(Icons.person, size: 12, color: Colors.grey),
            const SizedBox(width: 4),
            Text('${t.empName} · ${t.empDept}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
            const Spacer(),
            Text(t.dateRaised.length > 10 ? t.dateRaised.substring(0,10) : t.dateRaised,
                style: const TextStyle(color: Colors.grey, fontSize: 10)),
          ]),
          if (t.expectedResolution.isNotEmpty || t.empFeedbackStatus.isNotEmpty) ...[
            const SizedBox(height: 4),
            Row(children: [
              if (t.expectedResolution.isNotEmpty)
                Expanded(child: Text('⏱ ETA: ${t.expectedResolution}',
                    style: const TextStyle(color: Color(0xFFD97706), fontSize: 10, fontWeight: FontWeight.w600),
                    overflow: TextOverflow.ellipsis)),
              if (t.empFeedbackStatus.isNotEmpty)
                Text(t.empFeedbackStatus == 'Resolved' ? '😊 Feedback: OK' : '😞 Feedback: Issue',
                    style: TextStyle(color: t.empFeedbackStatus == 'Resolved' ? const Color(0xFF059669) : const Color(0xFFDC2626),
                        fontSize: 10, fontWeight: FontWeight.w600)),
            ]),
          ] else if (t.status == 'Resolved' || t.status == 'Closed') ...[
            const SizedBox(height: 4),
            const Text('Feedback pending', style: TextStyle(color: Color(0xFFDC2626), fontSize: 10, fontWeight: FontWeight.w600)),
          ],
        ]),
      ),
    );
  }
}
