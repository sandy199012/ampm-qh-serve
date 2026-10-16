import 'package:flutter/material.dart';
import '../services/api_service.dart';

// Mirrors AMPMTool desktop's "Purchases" tab (kv key "asset_purchases") —
// a simple purchase log, separate from formal Purchase Orders.
class PurchasesScreen extends StatefulWidget {
  const PurchasesScreen({super.key});
  @override
  State<PurchasesScreen> createState() => _PurchasesScreenState();
}

class _PurchasesScreenState extends State<PurchasesScreen> {
  static const _categories = ['Hardware','Software','Accessories','Networking','Furniture','Other'];
  static const _payments = ['Cash','Card','UPI','Bank Transfer','Cheque','Pending'];

  List<Map<String, dynamic>> _purchases = [];
  bool _loading = true;
  String? _error;
  final _search = TextEditingController();

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final p = await ApiService.getPurchases();
      p.sort((a, b) => (b['date'] ?? '').toString().compareTo((a['date'] ?? '').toString()));
      setState(() { _purchases = p; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  List<Map<String, dynamic>> get _filtered {
    if (_search.text.isEmpty) return _purchases;
    final q = _search.text.toLowerCase();
    return _purchases.where((p) =>
      (p['description'] ?? '').toString().toLowerCase().contains(q) ||
      (p['vendor'] ?? '').toString().toLowerCase().contains(q) ||
      (p['category'] ?? '').toString().toLowerCase().contains(q)).toList();
  }

  double get _totalThisList => _filtered.fold(0.0, (s, p) => s + ((p['totalCost'] ?? 0) as num).toDouble());

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF5F2FF),
      appBar: AppBar(
        title: const Text('Purchases'),
        backgroundColor: const Color(0xFF130A33),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        Container(color: Colors.white, padding: const EdgeInsets.all(12), child: Column(children: [
          TextField(controller: _search, onChanged: (_) => setState(() {}),
            decoration: InputDecoration(hintText: 'Search purchase...',
              prefixIcon: const Icon(Icons.search, color: Colors.grey),
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
              contentPadding: const EdgeInsets.symmetric(vertical: 8), isDense: true)),
        ])),
        if (!_loading && _error == null)
          Container(width: double.infinity, color: const Color(0xFFF0EBFF), padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Text('Total: ₹${_totalThisList.toStringAsFixed(0)}  (${_filtered.length} purchases)',
              style: const TextStyle(color: Color(0xFF6D4AFF), fontWeight: FontWeight.bold, fontSize: 12))),
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
              ? const Center(child: Text('No purchases found', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _filtered.length, itemBuilder: (_, i) => _card(_filtered[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New Purchase', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _card(Map<String, dynamic> p) {
    return GestureDetector(
      onTap: () => _showDetail(p),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: const Border(left: BorderSide(color: Color(0xFF059669), width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((p['description'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            Text('₹${p['totalCost'] ?? 0}', style: const TextStyle(fontWeight: FontWeight.bold, color: Color(0xFF059669), fontSize: 13)),
          ]),
          Text('${p['category'] ?? ''} · ${p['vendor'] ?? ''} · Qty: ${p['qty'] ?? 1}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
          Text(p['date'] ?? '', style: const TextStyle(color: Colors.grey, fontSize: 10)),
        ])),
    );
  }

  Widget _row(String l, dynamic v) {
    final s = (v ?? '').toString();
    if (s.isEmpty) return const SizedBox.shrink();
    return Padding(padding: const EdgeInsets.only(bottom: 6), child: Row(
      crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(width: 100, child: Text('$l:', style: const TextStyle(color: Colors.grey, fontSize: 12, fontWeight: FontWeight.w600))),
        Expanded(child: Text(s, style: const TextStyle(fontSize: 13))),
      ]));
  }

  void _showDetail(Map<String, dynamic> p) {
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
          Text((p['description'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          const SizedBox(height: 12),
          _row('Category', p['category']), _row('Budget Item', p['budgetItem']), _row('Vendor', p['vendor']),
          _row('Qty', p['qty']), _row('Unit Cost', (p['unitCost'] ?? 0) != 0 ? '₹${p['unitCost']}' : ''),
          _row('Total Cost', (p['totalCost'] ?? 0) != 0 ? '₹${p['totalCost']}' : ''),
          _row('Date', p['date']), _row('Invoice', p['invoice']), _row('Payment', p['payment']),
          _row('Assigned To', p['assignedTo']), _row('Notes', p['notes']),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEdit(p); },
              icon: const Icon(Icons.edit), label: const Text('Edit'))),
            const SizedBox(width: 10),
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _confirmDelete(p); },
              icon: const Icon(Icons.delete_outline, color: Color(0xFFDC2626)),
              label: const Text('Delete', style: TextStyle(color: Color(0xFFDC2626))),
              style: OutlinedButton.styleFrom(side: const BorderSide(color: Color(0xFFDC2626))))),
          ]),
        ])),
      ),
    );
  }

  Future<void> _confirmDelete(Map<String, dynamic> p) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Delete this purchase?'),
      content: Text('${p['description']} will be permanently deleted.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        TextButton(onPressed: () => Navigator.pop(context, true),
          child: const Text('Delete', style: TextStyle(color: Colors.red, fontWeight: FontWeight.bold))),
      ]));
    if (ok != true) return;
    try {
      await ApiService.deletePurchase(p['id'].toString());
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Purchase deleted'), backgroundColor: Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  void _showEdit(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final desc = TextEditingController(text: (existing?['description'] ?? '').toString());
    final budgetItem = TextEditingController(text: (existing?['budgetItem'] ?? '').toString());
    final vendor = TextEditingController(text: (existing?['vendor'] ?? '').toString());
    final qty = TextEditingController(text: (existing?['qty'] ?? 1).toString());
    final unitCost = TextEditingController(text: (existing?['unitCost'] ?? 0).toString());
    final date = TextEditingController(text: (existing?['date'] ?? DateTime.now().toString().split(' ')[0]).toString());
    final invoice = TextEditingController(text: (existing?['invoice'] ?? '').toString());
    final assignedTo = TextEditingController(text: (existing?['assignedTo'] ?? '').toString());
    final notes = TextEditingController(text: (existing?['notes'] ?? '').toString());
    String category = _categories.contains(existing?['category']) ? existing!['category'] : _categories[0];
    String payment = _payments.contains(existing?['payment']) ? existing!['payment'] : _payments[0];
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(isEdit ? 'Edit Purchase' : 'New Purchase', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: desc, decoration: const InputDecoration(labelText: 'Description *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          DropdownButtonFormField<String>(value: category, isExpanded: true,
            items: _categories.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
            onChanged: (v) => setS(() => category = v!),
            decoration: const InputDecoration(labelText: 'Category', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: budgetItem, decoration: const InputDecoration(labelText: 'Budget Item (optional)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: vendor, decoration: const InputDecoration(labelText: 'Vendor', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          Row(children: [
            Expanded(child: TextField(controller: qty, keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Qty', border: OutlineInputBorder()))),
            const SizedBox(width: 8),
            Expanded(child: TextField(controller: unitCost, keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Unit Cost (₹)', border: OutlineInputBorder()))),
          ]),
          const SizedBox(height: 10),
          TextField(controller: date, decoration: const InputDecoration(labelText: 'Date (yyyy-mm-dd)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: invoice, decoration: const InputDecoration(labelText: 'Invoice No', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          DropdownButtonFormField<String>(value: payment, isExpanded: true,
            items: _payments.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
            onChanged: (v) => setS(() => payment = v!),
            decoration: const InputDecoration(labelText: 'Payment Mode', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: assignedTo, decoration: const InputDecoration(labelText: 'Assigned To (optional)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: notes, maxLines: 2, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (desc.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Description required!'))); return; }
              setS(() => saving = true);
              try {
                final q = int.tryParse(qty.text.trim()) ?? 1;
                final uc = double.tryParse(unitCost.text.trim()) ?? 0;
                final dt = DateTime.tryParse(date.text.trim()) ?? DateTime.now();
                final data = {
                  if (isEdit) 'id': existing!['id'],
                  if (isEdit) 'addedToStock': existing!['addedToStock'] ?? false,
                  'description': desc.text.trim(), 'category': category, 'budgetItem': budgetItem.text.trim(),
                  'vendor': vendor.text.trim(), 'qty': q, 'unitCost': uc, 'totalCost': q * uc,
                  'date': date.text.trim(), 'month': '${_monthAbbr(dt.month)}${dt.year.toString().substring(2)}',
                  'invoice': invoice.text.trim(), 'payment': payment,
                  'assignedTo': assignedTo.text.trim(), 'notes': notes.text.trim(),
                };
                await ApiService.savePurchase(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(isEdit ? 'Purchase updated!' : 'Purchase added!'), backgroundColor: const Color(0xFF059669)));
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

  String _monthAbbr(int m) =>
    const ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][m - 1];
}
