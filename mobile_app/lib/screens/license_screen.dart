import 'package:flutter/material.dart';
import '../services/api_service.dart';

class LicenseScreen extends StatefulWidget {
  const LicenseScreen({super.key});
  @override
  State<LicenseScreen> createState() => _LicenseScreenState();
}

class _LicenseScreenState extends State<LicenseScreen> {
  static const _categories = ['Software','Antivirus','Cloud','Subscription','OS','Other'];
  static const _statuses = ['All','Active','Expired','Cancelled','Suspended'];

  List<Map<String, dynamic>> _licenses = [];
  bool _loading = true;
  String? _error;
  final _search = TextEditingController();
  String _status = 'All';

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final l = await ApiService.getLicenses();
      setState(() { _licenses = l; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  int? _daysToRenewal(Map<String, dynamic> l) {
    final d = DateTime.tryParse((l['renewalDate'] ?? '').toString());
    if (d == null) return null;
    return d.difference(DateTime(DateTime.now().year, DateTime.now().month, DateTime.now().day)).inDays;
  }

  List<Map<String, dynamic>> get _filtered {
    var list = _licenses.toList();
    if (_status != 'All') list = list.where((l) => (l['status'] ?? '') == _status).toList();
    if (_search.text.isNotEmpty) {
      final q = _search.text.toLowerCase();
      list = list.where((l) =>
        (l['name'] ?? '').toString().toLowerCase().contains(q) ||
        (l['vendor'] ?? '').toString().toLowerCase().contains(q)).toList();
    }
    return list;
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: const Text('Licenses'),
        backgroundColor: const Color(0xFF0A192F),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        Container(color: Colors.white, padding: const EdgeInsets.all(12), child: Column(children: [
          TextField(controller: _search, onChanged: (_) => setState(() {}),
            decoration: InputDecoration(hintText: 'Search license...',
              prefixIcon: const Icon(Icons.search, color: Colors.grey),
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
              contentPadding: const EdgeInsets.symmetric(vertical: 8), isDense: true)),
          const SizedBox(height: 8),
          SizedBox(height: 34, child: ListView(scrollDirection: Axis.horizontal,
            children: _statuses.map((s) => Padding(
              padding: const EdgeInsets.only(right: 8),
              child: FilterChip(
                label: Text(s, style: TextStyle(color: _status == s ? Colors.white : const Color(0xFF374151), fontWeight: FontWeight.w600, fontSize: 12)),
                selected: _status == s,
                onSelected: (_) => setState(() => _status = s),
                selectedColor: const Color(0xFF2563EB),
                backgroundColor: const Color(0xFFF3F4F6), side: BorderSide.none,
              ))).toList())),
        ])),
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
              ? const Center(child: Text('No licenses found', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _filtered.length, itemBuilder: (_, i) => _card(_filtered[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New License', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _card(Map<String, dynamic> l) {
    final days = _daysToRenewal(l);
    final expired = days != null && days < 0;
    final soon = days != null && days >= 0 && days <= ((l['alertDays'] ?? 30) as num).toInt();
    final c = expired ? const Color(0xFFDC2626) : soon ? const Color(0xFFD97706) : const Color(0xFF059669);
    return GestureDetector(
      onTap: () => _showDetail(l),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: c, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((l['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: c.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text((l['status'] ?? '').toString(), style: TextStyle(color: c, fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          Text('${l['vendor'] ?? ''} · ${l['category'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
          const SizedBox(height: 4),
          Text('Renewal: ${l['renewalDate'] ?? '—'}${days != null ? (expired ? "  (EXPIRED)" : "  ($days days)") : ""}',
            style: TextStyle(color: c, fontSize: 11, fontWeight: FontWeight.w600)),
          if ((l['cost'] ?? 0) != 0)
            Text('₹${l['cost']}/yr · ${l['seats'] ?? 0} seats', style: const TextStyle(color: Colors.grey, fontSize: 11)),
        ])),
    );
  }

  Widget _row(String l, dynamic v) {
    final s = (v ?? '').toString();
    if (s.isEmpty) return const SizedBox.shrink();
    return Padding(padding: const EdgeInsets.only(bottom: 6), child: Row(
      crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(width: 120, child: Text('$l:', style: const TextStyle(color: Colors.grey, fontSize: 12, fontWeight: FontWeight.w600))),
        Expanded(child: Text(s, style: const TextStyle(fontSize: 13))),
      ]));
  }

  void _showDetail(Map<String, dynamic> l) {
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
          Text((l['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          const SizedBox(height: 12),
          _row('Vendor', l['vendor']), _row('Category', l['category']), _row('Seats', l['seats']),
          _row('Purchase Date', l['purchaseDate']), _row('Renewal Date', l['renewalDate']),
          _row('Last Renewed', '${l['lastRenewedDate'] ?? ''} ${(l['lastRenewedBy'] ?? '').toString().isNotEmpty ? "by ${l['lastRenewedBy']}" : ""}'.trim()),
          _row('Cost/yr', (l['cost'] ?? 0) != 0 ? '₹${l['cost']}' : ''),
          _row('Status', l['status']), _row('License Key', l['licenseKey']),
          _row('Invoice No', l['invoiceNo']), _row('Notes', l['notes']),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEdit(l); },
              icon: const Icon(Icons.edit), label: const Text('Edit'))),
            const SizedBox(width: 10),
            Expanded(child: ElevatedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showRenew(l); },
              icon: const Icon(Icons.autorenew), label: const Text('Renew'),
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white))),
          ]),
        ])),
      ),
    );
  }

  void _showRenew(Map<String, dynamic> l) {
    final newDate = TextEditingController(text: (l['renewalDate'] ?? '').toString());
    final cost = TextEditingController(text: (l['cost'] ?? 0).toString());
    bool saving = false;
    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text('Renew — ${l['name']}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: newDate, decoration: const InputDecoration(labelText: 'New Renewal Date (yyyy-mm-dd) *', border: OutlineInputBorder()),
            onTap: () async {
              final picked = await showDatePicker(context: ctx, initialDate: DateTime.now(),
                firstDate: DateTime(2020), lastDate: DateTime(2100));
              if (picked != null) newDate.text = picked.toString().split(' ')[0];
            }, readOnly: true),
          const SizedBox(height: 10),
          TextField(controller: cost, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Cost/yr (₹)', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (newDate.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Enter a date!'))); return; }
              setS(() => saving = true);
              try {
                final data = Map<String, dynamic>.from(l);
                data['lastRenewedDate'] = l['renewalDate'];
                data['lastRenewedBy'] = 'IT Admin';
                data['renewalDate'] = newDate.text.trim();
                data['cost'] = double.tryParse(cost.text.trim()) ?? l['cost'];
                data['status'] = 'Active';
                await ApiService.saveLicense(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('License renewed!'), backgroundColor: Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Renew', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ]),
      )));
  }

  void _showEdit(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final name = TextEditingController(text: (existing?['name'] ?? '').toString());
    final vendor = TextEditingController(text: (existing?['vendor'] ?? '').toString());
    final seats = TextEditingController(text: (existing?['seats'] ?? 1).toString());
    final purchaseDate = TextEditingController(text: (existing?['purchaseDate'] ?? '').toString());
    final renewalDate = TextEditingController(text: (existing?['renewalDate'] ?? '').toString());
    final cost = TextEditingController(text: (existing?['cost'] ?? 0).toString());
    final key = TextEditingController(text: (existing?['licenseKey'] ?? '').toString());
    final invoice = TextEditingController(text: (existing?['invoiceNo'] ?? '').toString());
    final alertDays = TextEditingController(text: (existing?['alertDays'] ?? 30).toString());
    final notes = TextEditingController(text: (existing?['notes'] ?? '').toString());
    String category = _categories.contains(existing?['category']) ? existing!['category'] : _categories[0];
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(isEdit ? 'Edit License' : 'New License', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: name, decoration: const InputDecoration(labelText: 'License Name *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: vendor, decoration: const InputDecoration(labelText: 'Vendor', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          DropdownButtonFormField<String>(value: category, isExpanded: true,
            items: _categories.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
            onChanged: (v) => setS(() => category = v!),
            decoration: const InputDecoration(labelText: 'Category', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: seats, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Seats', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: purchaseDate, decoration: const InputDecoration(labelText: 'Purchase Date (yyyy-mm-dd)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: renewalDate, decoration: const InputDecoration(labelText: 'Renewal Date (yyyy-mm-dd) *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: cost, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Cost/yr (₹)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: alertDays, keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Alert Days Before Renewal', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: key, decoration: const InputDecoration(labelText: 'License Key', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: invoice, decoration: const InputDecoration(labelText: 'Invoice No', border: OutlineInputBorder())),
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
                  if (isEdit) 'lastRenewedDate': existing!['lastRenewedDate'] ?? '',
                  if (isEdit) 'lastRenewedBy': existing!['lastRenewedBy'] ?? '',
                  if (isEdit) 'status': existing!['status'] ?? 'Active',
                  'name': name.text.trim(), 'vendor': vendor.text.trim(), 'category': category,
                  'seats': int.tryParse(seats.text.trim()) ?? 0,
                  'purchaseDate': purchaseDate.text.trim(), 'renewalDate': renewalDate.text.trim(),
                  'cost': double.tryParse(cost.text.trim()) ?? 0,
                  'alertDays': int.tryParse(alertDays.text.trim()) ?? 30,
                  'licenseKey': key.text.trim(), 'invoiceNo': invoice.text.trim(), 'notes': notes.text.trim(),
                };
                await ApiService.saveLicense(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(isEdit ? 'License updated!' : 'License added!'), backgroundColor: const Color(0xFF059669)));
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
