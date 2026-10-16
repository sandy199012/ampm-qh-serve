import 'package:flutter/material.dart';
import '../services/api_service.dart';

// Mirrors AMPMTool desktop's "Purchase Bills" tab (kv key "purchase_bills").
// Saving a bill linked to a Budget item posts the amount into that budget's
// monthly Actual, and linking a PO auto-marks it "Received" — same as desktop.
class PurchaseBillsScreen extends StatefulWidget {
  const PurchaseBillsScreen({super.key});
  @override
  State<PurchaseBillsScreen> createState() => _PurchaseBillsScreenState();
}

class _PurchaseBillsScreenState extends State<PurchaseBillsScreen> {
  static const _categories = ['Hardware','Software','AMC','Networking','Misc'];
  static const _paymentModes = ['Bank Transfer','Cheque','Cash','UPI','Card','Pending'];

  List<Map<String, dynamic>> _bills = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final b = await ApiService.getPurchaseBills();
      b.sort((a, c) => (c['billDate'] ?? '').toString().compareTo((a['billDate'] ?? '').toString()));
      setState(() { _bills = b; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  @override
  Widget build(BuildContext context) {
    final total = _bills.fold(0.0, (s, b) => s + ((b['totalAmount'] ?? 0) as num).toDouble());
    return Scaffold(
      backgroundColor: const Color(0xFFF5F2FF),
      appBar: AppBar(
        title: const Text('Purchase Bills'),
        backgroundColor: const Color(0xFF130A33),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        if (!_loading && _error == null)
          Container(width: double.infinity, color: const Color(0xFFF0EBFF), padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Text('Total: ₹${total.toStringAsFixed(0)}  (${_bills.length} bills)',
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
            : _bills.isEmpty
              ? const Center(child: Text('No purchase bills yet', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _bills.length, itemBuilder: (_, i) => _card(_bills[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('Nayi Bill', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _card(Map<String, dynamic> b) {
    return GestureDetector(
      onTap: () => _showDetail(b),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: const Border(left: BorderSide(color: Color(0xFF6D4AFF), width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((b['description'] ?? b['billNo'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            Text('₹${b['totalAmount'] ?? 0}', style: const TextStyle(fontWeight: FontWeight.bold, color: Color(0xFF059669), fontSize: 13)),
          ]),
          Text('${b['vendorName'] ?? ''}${(b['poNumber'] ?? '').toString().isNotEmpty ? "  ·  PO: ${b['poNumber']}" : ""}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
          Text('${b['billDate'] ?? ''}  ·  ${b['category'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 10)),
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

  void _showDetail(Map<String, dynamic> b) {
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
          Text((b['description'] ?? b['billNo'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          const SizedBox(height: 12),
          _row('Bill No', b['billNo']), _row('Bill Date', b['billDate']), _row('PO Number', b['poNumber']),
          _row('Vendor', b['vendorName']), _row('Budget', b['budgetDesc']), _row('Category', b['category']),
          _row('Amount', (b['amount'] ?? 0) != 0 ? '₹${b['amount']}' : ''),
          _row('GST', (b['gstAmount'] ?? 0) != 0 ? '₹${b['gstAmount']}' : ''),
          _row('Total', (b['totalAmount'] ?? 0) != 0 ? '₹${b['totalAmount']}' : ''),
          _row('Payment Mode', b['paymentMode']), _row('Payment Ref', b['paymentRef']),
          _row('Status', b['status']), _row('Entered By', '${b['enteredBy'] ?? ''} ${b['enteredOn'] ?? ''}'.trim()),
          _row('Notes', b['notes']),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEdit(b); },
              icon: const Icon(Icons.edit), label: const Text('Edit'))),
            const SizedBox(width: 10),
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _confirmDelete(b); },
              icon: const Icon(Icons.delete_outline, color: Color(0xFFDC2626)),
              label: const Text('Delete', style: TextStyle(color: Color(0xFFDC2626))),
              style: OutlinedButton.styleFrom(side: const BorderSide(color: Color(0xFFDC2626))))),
          ]),
        ])),
      ),
    );
  }

  Future<void> _confirmDelete(Map<String, dynamic> b) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Delete this bill?'),
      content: const Text('Deleting this bill will also revert the linked budget actual.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        TextButton(onPressed: () => Navigator.pop(context, true),
          child: const Text('Delete', style: TextStyle(color: Colors.red, fontWeight: FontWeight.bold))),
      ]));
    if (ok != true) return;
    try {
      await ApiService.deletePurchaseBill(b['id'].toString());
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Bill deleted'), backgroundColor: Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  void _showEdit(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final billNo = TextEditingController(text: (existing?['billNo'] ?? '').toString());
    final billDate = TextEditingController(text: (existing?['billDate'] ?? DateTime.now().toString().split(' ')[0]).toString());
    final poNumber = TextEditingController(text: (existing?['poNumber'] ?? '').toString());
    final vendorName = TextEditingController(text: (existing?['vendorName'] ?? '').toString());
    final amount = TextEditingController(text: (existing?['amount'] ?? 0).toString());
    final gstAmount = TextEditingController(text: (existing?['gstAmount'] ?? 0).toString());
    final description = TextEditingController(text: (existing?['description'] ?? '').toString());
    final paymentRef = TextEditingController(text: (existing?['paymentRef'] ?? '').toString());
    final notes = TextEditingController(text: (existing?['notes'] ?? '').toString());
    String category = _categories.contains(existing?['category']) ? existing!['category'] : _categories[0];
    String paymentMode = _paymentModes.contains(existing?['paymentMode']) ? existing!['paymentMode'] : _paymentModes[0];
    String? budgetId = existing?['budgetId'];
    String budgetDescText = (existing?['budgetDesc'] ?? '').toString();
    List<Map<String, dynamic>> pos = [];
    List<Map<String, dynamic>> budgetList = [];
    bool saving = false;

    Future<void> loadPickers(void Function(void Function()) setS) async {
      try { pos = await ApiService.getPOs(); } catch (_) {}
      try { budgetList = await ApiService.getBudget(); } catch (_) {}
      setS(() {});
    }

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) {
        if (pos.isEmpty && budgetList.isEmpty) loadPickers(setS);
        return Padding(
          padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
          child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Expanded(child: Text(isEdit ? 'Edit Bill' : 'New Purchase Bill', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
              IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
            ]),
            const SizedBox(height: 12),
            TextField(controller: description, decoration: const InputDecoration(labelText: 'Description *', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            TextField(controller: billNo, decoration: const InputDecoration(labelText: 'Bill No', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            TextField(controller: billDate, decoration: const InputDecoration(labelText: 'Bill Date (yyyy-mm-dd) *', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            if (pos.isNotEmpty)
              DropdownButtonFormField<String>(
                value: pos.any((p) => p['poNumber'] == poNumber.text) ? poNumber.text : null,
                isExpanded: true, hint: const Text('Link PO (optional)'),
                items: pos.map((p) => DropdownMenuItem<String>(value: (p['poNumber'] ?? '').toString(),
                  child: Text('${p['poNumber']} — ${p['vendorName']}', overflow: TextOverflow.ellipsis))).toList(),
                onChanged: (v) {
                  final po = pos.firstWhere((x) => x['poNumber'] == v, orElse: () => {});
                  setS(() { poNumber.text = v ?? ''; vendorName.text = (po['vendorName'] ?? '').toString(); });
                },
                decoration: const InputDecoration(labelText: 'Link Purchase Order', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            TextField(controller: vendorName, decoration: const InputDecoration(labelText: 'Vendor Name', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(value: category, isExpanded: true,
              items: _categories.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => category = v!),
              decoration: const InputDecoration(labelText: 'Category', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            if (budgetList.isNotEmpty)
              DropdownButtonFormField<String>(
                value: budgetList.any((b) => b['id'] == budgetId) ? budgetId : null,
                isExpanded: true, hint: const Text('Link Budget Item (optional)'),
                items: budgetList.map((b) => DropdownMenuItem<String>(value: (b['id'] ?? '').toString(), child: Text((b['description'] ?? '').toString()))).toList(),
                onChanged: (v) {
                  final b = budgetList.firstWhere((x) => x['id'] == v, orElse: () => {});
                  setS(() { budgetId = v; budgetDescText = (b['description'] ?? '').toString(); });
                },
                decoration: const InputDecoration(labelText: 'Link Budget Item', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            Row(children: [
              Expanded(child: TextField(controller: amount, keyboardType: TextInputType.number,
                decoration: const InputDecoration(labelText: 'Amount (₹) *', border: OutlineInputBorder()))),
              const SizedBox(width: 8),
              Expanded(child: TextField(controller: gstAmount, keyboardType: TextInputType.number,
                decoration: const InputDecoration(labelText: 'GST (₹)', border: OutlineInputBorder()))),
            ]),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(value: paymentMode, isExpanded: true,
              items: _paymentModes.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => paymentMode = v!),
              decoration: const InputDecoration(labelText: 'Payment Mode', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            TextField(controller: paymentRef, decoration: const InputDecoration(labelText: 'Payment Ref', border: OutlineInputBorder())),
            const SizedBox(height: 10),
            TextField(controller: notes, maxLines: 2, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
            const SizedBox(height: 16),
            SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
              onPressed: saving ? null : () async {
                if (description.text.trim().isEmpty || billDate.text.trim().isEmpty) {
                  ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Description and Bill Date are required!'))); return;
                }
                setS(() => saving = true);
                try {
                  final amt = double.tryParse(amount.text.trim()) ?? 0;
                  final gAmt = double.tryParse(gstAmount.text.trim()) ?? 0;
                  final dt = DateTime.tryParse(billDate.text.trim()) ?? DateTime.now();
                  final data = {
                    if (isEdit) 'id': existing!['id'],
                    'billNo': billNo.text.trim(), 'billDate': billDate.text.trim(),
                    'poNumber': poNumber.text.trim(), 'poId': poNumber.text.trim(),
                    'vendorName': vendorName.text.trim(), 'budgetId': budgetId ?? '', 'budgetDesc': budgetDescText,
                    'amount': amt, 'gstAmount': gAmt, 'totalAmount': amt + gAmt,
                    'month': '${_monthAbbr(dt.month)}${dt.year.toString().substring(2)}',
                    'description': description.text.trim(), 'category': category,
                    'paymentMode': paymentMode, 'paymentRef': paymentRef.text.trim(),
                    'status': 'Pending', 'notes': notes.text.trim(),
                  };
                  await ApiService.savePurchaseBill(data);
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
                : Text(isEdit ? 'Update' : 'Save Bill', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
            )),
            const SizedBox(height: 8),
          ])),
        );
      }));
  }

  String _monthAbbr(int m) =>
    const ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][m - 1];
}
