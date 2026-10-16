import 'package:flutter/material.dart';
import '../services/api_service.dart';

class BillsScreen extends StatefulWidget {
  const BillsScreen({super.key});
  @override
  State<BillsScreen> createState() => _BillsScreenState();
}

class _BillsScreenState extends State<BillsScreen> {
  static const _categories = ['Internet','Electricity','Telephone','AMC','Rent','Other'];
  static const _statuses = ['All','Pending','Submitted','Paid','Overdue','On Hold'];

  List<Map<String, dynamic>> _bills = [];
  bool _loading = true;
  String? _error;
  String _status = 'All';

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final b = await ApiService.getBills();
      setState(() { _bills = b; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  int? _daysToDue(Map<String, dynamic> b) {
    final d = DateTime.tryParse((b['dueDate'] ?? '').toString());
    if (d == null) return null;
    return d.difference(DateTime(DateTime.now().year, DateTime.now().month, DateTime.now().day)).inDays;
  }

  List<Map<String, dynamic>> get _filtered {
    if (_status == 'All') return _bills;
    return _bills.where((b) => (b['status'] ?? '') == _status).toList();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: const Text('Bills'),
        backgroundColor: const Color(0xFF0A192F),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        Container(color: Colors.white, padding: const EdgeInsets.all(12),
          child: SizedBox(height: 34, child: ListView(scrollDirection: Axis.horizontal,
            children: _statuses.map((s) => Padding(
              padding: const EdgeInsets.only(right: 8),
              child: FilterChip(
                label: Text(s, style: TextStyle(color: _status == s ? Colors.white : const Color(0xFF374151), fontWeight: FontWeight.w600, fontSize: 12)),
                selected: _status == s,
                onSelected: (_) => setState(() => _status = s),
                selectedColor: const Color(0xFF2563EB),
                backgroundColor: const Color(0xFFF3F4F6), side: BorderSide.none,
              ))).toList()))),
        Expanded(child: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
            ? Center(child: Padding(padding: const EdgeInsets.all(24), child: Column(
                mainAxisAlignment: MainAxisAlignment.center, children: [
                  const Icon(Icons.wifi_off, size: 48, color: Color(0xFFDC2626)),
                  const SizedBox(height: 12),
                  Text(_error!, style: const TextStyle(color: Colors.grey, fontSize: 12), textAlign: TextAlign.center),
                  const SizedBox(height: 12),
                  ElevatedButton(onPressed: _load, child: const Text('Retry')),
                ])))
            : _filtered.isEmpty
              ? const Center(child: Text('No bills found', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _filtered.length, itemBuilder: (_, i) => _card(_filtered[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New Bill', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Color _statusColor(String s) {
    switch (s) {
      case 'Paid':      return const Color(0xFF059669);
      case 'Submitted': return const Color(0xFF2563EB);
      case 'Overdue':   return const Color(0xFFDC2626);
      case 'On Hold':   return const Color(0xFF6B7280);
      default:          return const Color(0xFFD97706);
    }
  }

  Widget _card(Map<String, dynamic> b) {
    final status = (b['status'] ?? '').toString();
    final days = _daysToDue(b);
    final overdue = status != 'Paid' && days != null && days < 0;
    final c = overdue ? const Color(0xFFDC2626) : _statusColor(status);
    return GestureDetector(
      onTap: () => _showDetail(b),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: c, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((b['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: c.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text(overdue ? 'OVERDUE' : status, style: TextStyle(color: c, fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          Text('${b['category'] ?? ''} · ${b['place'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
          const SizedBox(height: 4),
          Text('Due: ${b['dueDate'] ?? '—'}  ·  ₹${b['amount'] ?? 0}', style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
        ])),
    );
  }

  Widget _row(String l, dynamic v) {
    final s = (v ?? '').toString();
    if (s.isEmpty) return const SizedBox.shrink();
    return Padding(padding: const EdgeInsets.only(bottom: 6), child: Row(
      crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(width: 110, child: Text('$l:', style: const TextStyle(color: Colors.grey, fontSize: 12, fontWeight: FontWeight.w600))),
        Expanded(child: Text(s, style: const TextStyle(fontSize: 13))),
      ]));
  }

  void _showDetail(Map<String, dynamic> b) {
    final status = (b['status'] ?? '').toString();
    showModalBottomSheet(
      context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => Padding(
        padding: EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.of(ctx).viewInsets.bottom + 20),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            const Spacer(),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          Text((b['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          const SizedBox(height: 12),
          _row('Category', b['category']), _row('Place', b['place']), _row('Period', b['period']),
          _row('Due Date', b['dueDate']), _row('Amount', (b['amount'] ?? 0) != 0 ? '₹${b['amount']}' : ''),
          _row('Status', b['status']), _row('Submitted', '${b['submittedDate'] ?? ''} ${b['submittedBy'] ?? ''}'.trim()),
          _row('Paid', '${b['paidDate'] ?? ''} ${b['paidBy'] ?? ''}'.trim()),
          _row('Payment Ref', b['paymentRef']), _row('Invoice No', b['invoiceNo']), _row('Notes', b['notes']),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEdit(b); },
              icon: const Icon(Icons.edit), label: const Text('Edit'))),
            const SizedBox(width: 10),
            if (status != 'Paid')
              Expanded(child: ElevatedButton.icon(
                onPressed: () { Navigator.pop(ctx); _markStatus(b, status == 'Pending' ? 'Submitted' : 'Paid'); },
                icon: const Icon(Icons.check_circle_outline),
                label: Text(status == 'Pending' ? 'Mark Submitted' : 'Mark Paid'),
                style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white))),
          ]),
        ])),
      ),
    );
  }

  Future<void> _markStatus(Map<String, dynamic> b, String newStatus) async {
    try {
      final data = Map<String, dynamic>.from(b);
      data['status'] = newStatus;
      final now = DateTime.now().toString().split(' ')[0];
      if (newStatus == 'Submitted') { data['submittedDate'] = now; data['submittedBy'] = 'IT Admin'; }
      if (newStatus == 'Paid') { data['paidDate'] = now; data['paidBy'] = 'IT Admin'; }
      await ApiService.saveBill(data);
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Bill marked $newStatus'), backgroundColor: const Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  void _showEdit(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final name = TextEditingController(text: (existing?['name'] ?? '').toString());
    final place = TextEditingController(text: (existing?['place'] ?? '').toString());
    final dueDate = TextEditingController(text: (existing?['dueDate'] ?? '').toString());
    final amount = TextEditingController(text: (existing?['amount'] ?? 0).toString());
    final period = TextEditingController(text: (existing?['period'] ?? '').toString());
    final invoiceNo = TextEditingController(text: (existing?['invoiceNo'] ?? '').toString());
    final alertDays = TextEditingController(text: (existing?['alertDays'] ?? 3).toString());
    final notes = TextEditingController(text: (existing?['notes'] ?? '').toString());
    String category = _categories.contains(existing?['category']) ? existing!['category'] : _categories[0];
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(isEdit ? 'Edit Bill' : 'New Bill', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: name, decoration: const InputDecoration(labelText: 'Bill Name *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          DropdownButtonFormField<String>(value: category, isExpanded: true,
            items: _categories.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
            onChanged: (v) => setS(() => category = v!),
            decoration: const InputDecoration(labelText: 'Category', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: place, decoration: const InputDecoration(labelText: 'Place / Location', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: dueDate, decoration: const InputDecoration(labelText: 'Due Date (yyyy-mm-dd) *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: amount, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Amount (₹)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: period, decoration: const InputDecoration(labelText: 'Period (e.g. Jul 2026)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: alertDays, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Alert Days Before Due', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: invoiceNo, decoration: const InputDecoration(labelText: 'Invoice No', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: notes, maxLines: 2, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (name.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Name required!'))); return; }
              setS(() => saving = true);
              try {
                final data = {
                  if (isEdit) 'id': existing!['id'],
                  if (isEdit) 'status': existing!['status'] ?? 'Pending',
                  if (isEdit) 'submittedDate': existing!['submittedDate'] ?? '',
                  if (isEdit) 'submittedBy': existing!['submittedBy'] ?? '',
                  if (isEdit) 'paidDate': existing!['paidDate'] ?? '',
                  if (isEdit) 'paidBy': existing!['paidBy'] ?? '',
                  if (isEdit) 'paymentRef': existing!['paymentRef'] ?? '',
                  'name': name.text.trim(), 'category': category, 'place': place.text.trim(),
                  'dueDate': dueDate.text.trim(), 'amount': double.tryParse(amount.text.trim()) ?? 0,
                  'period': period.text.trim(), 'alertDays': int.tryParse(alertDays.text.trim()) ?? 3,
                  'invoiceNo': invoiceNo.text.trim(), 'notes': notes.text.trim(),
                };
                await ApiService.saveBill(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(isEdit ? 'Bill updated!' : 'Bill added!'), backgroundColor: const Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : Text(isEdit ? 'Update' : 'Add', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ])),
      )));
  }
}
