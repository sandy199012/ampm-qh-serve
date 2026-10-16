import 'package:flutter/material.dart';
import '../services/api_service.dart';

// Mirrors AMPMTool desktop's Purchase Order tab (table "po_list") — PO
// number auto-generates as AMPM/IT/PO/<FY>/<serial> server-side (same
// financial-year logic as the desktop's NextPoNumber()).
class POScreen extends StatefulWidget {
  const POScreen({super.key});
  @override
  State<POScreen> createState() => _POScreenState();
}

class _POScreenState extends State<POScreen> {
  static const _statuses = ['All','Draft','Issued','Received','Cancelled'];
  static const _priorities = ['Normal','High','Urgent'];

  List<Map<String, dynamic>> _pos = [];
  bool _loading = true;
  String? _error;
  String _status = 'All';
  final _search = TextEditingController();

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final p = await ApiService.getPOs();
      setState(() { _pos = p; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  List<Map<String, dynamic>> get _filtered {
    var list = _pos.toList();
    if (_status != 'All') list = list.where((p) => (p['status'] ?? '') == _status).toList();
    if (_search.text.isNotEmpty) {
      final q = _search.text.toLowerCase();
      list = list.where((p) =>
        (p['poNumber'] ?? '').toString().toLowerCase().contains(q) ||
        (p['vendorName'] ?? '').toString().toLowerCase().contains(q) ||
        (p['purpose'] ?? '').toString().toLowerCase().contains(q)).toList();
    }
    return list;
  }

  Color _statusColor(String s) {
    switch (s) {
      case 'Received':  return const Color(0xFF059669);
      case 'Cancelled': return const Color(0xFFDC2626);
      case 'Issued':    return const Color(0xFF6D4AFF);
      default:          return const Color(0xFF6E6890); // Draft
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF5F2FF),
      appBar: AppBar(
        title: const Text('Purchase Orders'),
        backgroundColor: const Color(0xFF130A33),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        Container(color: Colors.white, padding: const EdgeInsets.all(12), child: Column(children: [
          TextField(controller: _search, onChanged: (_) => setState(() {}),
            decoration: InputDecoration(hintText: 'Search by PO number, vendor, purpose...',
              prefixIcon: const Icon(Icons.search, color: Colors.grey),
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
              contentPadding: const EdgeInsets.symmetric(vertical: 8), isDense: true)),
          const SizedBox(height: 8),
          SizedBox(height: 34, child: ListView(scrollDirection: Axis.horizontal,
            children: _statuses.map((s) => Padding(
              padding: const EdgeInsets.only(right: 8),
              child: FilterChip(
                label: Text(s, style: TextStyle(color: _status == s ? Colors.white : const Color(0xFF3B3556), fontWeight: FontWeight.w600, fontSize: 12)),
                selected: _status == s,
                onSelected: (_) => setState(() => _status = s),
                selectedColor: const Color(0xFF6D4AFF),
                backgroundColor: const Color(0xFFEFEAFB), side: BorderSide.none,
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
              ? const Center(child: Text('No POs found', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _filtered.length, itemBuilder: (_, i) => _card(_filtered[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New PO', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _card(Map<String, dynamic> p) {
    final status = (p['status'] ?? '').toString();
    final c = _statusColor(status);
    return GestureDetector(
      onTap: () => _showDetail(p),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: c, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((p['poNumber'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, color: Color(0xFF6D4AFF), fontSize: 12))),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: c.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text(status, style: TextStyle(color: c, fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          const SizedBox(height: 6),
          Text((p['vendorName'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
          if ((p['purpose'] ?? '').toString().isNotEmpty)
            Text((p['purpose'] ?? '').toString(), style: const TextStyle(color: Colors.grey, fontSize: 11)),
          const SizedBox(height: 4),
          Row(children: [
            Text('₹${((p['grandTotal'] ?? 0) as num).toStringAsFixed(0)}',
              style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13)),
            const SizedBox(width: 8),
            Text(p['poDate'] ?? '', style: const TextStyle(color: Colors.grey, fontSize: 10)),
          ]),
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

  void _showDetail(Map<String, dynamic> p) {
    final items = List<Map<String, dynamic>>.from((p['items'] ?? []) as List);
    showModalBottomSheet(
      context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => Padding(
        padding: EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.of(ctx).viewInsets.bottom + 20),
        child: SizedBox(height: MediaQuery.of(ctx).size.height * .85, child: SingleChildScrollView(
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              const Spacer(),
              IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
            ]),
            Text((p['poNumber'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18, color: Color(0xFF6D4AFF))),
            Text((p['vendorName'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15)),
            const SizedBox(height: 12),
            _row('Status', p['status']), _row('Priority', p['priority']), _row('PO Date', p['poDate']),
            _row('Delivery Date', p['deliveryDate']), _row('Department', p['dept']), _row('Purpose', p['purpose']),
            _row('Vendor GST', p['vendorGst']), _row('Vendor Phone', p['vendorPhone']),
            _row('Approved By', p['approvedBy']), _row('Requested By', p['requestedBy']),
            _row('Payment Terms', p['paymentTerms']), _row('Budget', p['budgetDesc']), _row('Notes', p['notes']),
            const SizedBox(height: 12),
            const Text('Items', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13)),
            const SizedBox(height: 6),
            ...items.map((it) => Padding(padding: const EdgeInsets.only(bottom: 6), child: Row(children: [
              Expanded(flex: 3, child: Text((it['desc'] ?? '').toString(), style: const TextStyle(fontSize: 12))),
              Expanded(child: Text('${it['qty']} × ₹${it['rate']}', style: const TextStyle(fontSize: 11, color: Colors.grey), textAlign: TextAlign.right)),
              SizedBox(width: 70, child: Text('₹${it['amount']}', style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600), textAlign: TextAlign.right)),
            ]))),
            const Divider(),
            Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
              const Text('Subtotal', style: TextStyle(fontSize: 12, color: Colors.grey)),
              Text('₹${p['subtotal'] ?? 0}', style: const TextStyle(fontSize: 12)),
            ]),
            Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
              const Text('GST', style: TextStyle(fontSize: 12, color: Colors.grey)),
              Text('₹${p['gstTotal'] ?? 0}', style: const TextStyle(fontSize: 12)),
            ]),
            Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
              const Text('Grand Total', style: TextStyle(fontSize: 14, fontWeight: FontWeight.bold)),
              Text('₹${p['grandTotal'] ?? 0}', style: const TextStyle(fontSize: 14, fontWeight: FontWeight.bold, color: Color(0xFF059669))),
            ]),
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
          ]),
        )),
      ),
    );
  }

  Future<void> _confirmDelete(Map<String, dynamic> p) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Delete this PO?'),
      content: Text('${p['poNumber']} will be permanently deleted.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        TextButton(onPressed: () => Navigator.pop(context, true),
          child: const Text('Delete', style: TextStyle(color: Colors.red, fontWeight: FontWeight.bold))),
      ]));
    if (ok != true) return;
    try {
      await ApiService.deletePO(p['poNumber'].toString());
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('PO deleted'), backgroundColor: Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  void _showEdit(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final vendorName = TextEditingController(text: (existing?['vendorName'] ?? '').toString());
    final vendorGst = TextEditingController(text: (existing?['vendorGst'] ?? '').toString());
    final vendorAddr = TextEditingController(text: (existing?['vendorAddr'] ?? '').toString());
    final vendorPhone = TextEditingController(text: (existing?['vendorPhone'] ?? '').toString());
    final vendorContact = TextEditingController(text: (existing?['vendorContact'] ?? '').toString());
    final purpose = TextEditingController(text: (existing?['purpose'] ?? '').toString());
    final dept = TextEditingController(text: (existing?['dept'] ?? 'IT').toString());
    final approvedBy = TextEditingController(text: (existing?['approvedBy'] ?? '').toString());
    final requestedBy = TextEditingController(text: (existing?['requestedBy'] ?? '').toString());
    final paymentTerms = TextEditingController(text: (existing?['paymentTerms'] ?? '').toString());
    final deliveryDate = TextEditingController(text: (existing?['deliveryDate'] ?? '').toString());
    final budgetDesc = TextEditingController(text: (existing?['budgetDesc'] ?? '').toString());
    String selectedBudgetId = (existing?['budgetId'] ?? '').toString();
    final notes = TextEditingController(text: (existing?['notes'] ?? '').toString());
    final terms = TextEditingController(text: (existing?['terms'] ?? '').toString());
    String status = _statuses.contains(existing?['status']) ? existing!['status'] : 'Draft';
    String priority = _priorities.contains(existing?['priority']) ? existing!['priority'] : 'Normal';
    List<Map<String, dynamic>> items = existing != null
        ? List<Map<String, dynamic>>.from((existing['items'] ?? []) as List)
        : [];
    List<Map<String, dynamic>> vendors = [];
    List<Map<String, dynamic>> budgetList = [];
    bool saving = false;

    Future<void> loadPickers(void Function(void Function()) setS) async {
      try { vendors = await ApiService.getVendors(); } catch (_) {}
      try { budgetList = await ApiService.getBudget(); } catch (_) {}
      setS(() {});
    }

    double subtotal() => items.fold(0.0, (s, it) => s + (((it['qty'] ?? 0) as num) * ((it['rate'] ?? 0) as num)).toDouble());
    double gstTotal() => items.fold(0.0, (s, it) => s + (((it['qty'] ?? 0) as num) * ((it['rate'] ?? 0) as num) * ((it['gst'] ?? 0) as num) / 100).toDouble());

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) {
        if (vendors.isEmpty && budgetList.isEmpty) loadPickers(setS);
        final sub = subtotal(); final gst = gstTotal();
        return Padding(
          padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
          child: SizedBox(height: MediaQuery.of(ctx).size.height * .88, child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Expanded(child: Text(isEdit ? 'Edit PO (${existing!['poNumber']})' : 'New Purchase Order', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
                IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
              ]),
              const SizedBox(height: 4),
              if (!isEdit) const Text('PO Number will be auto-generated', style: TextStyle(color: Colors.grey, fontSize: 11)),
              const SizedBox(height: 12),
              if (vendors.isNotEmpty)
                DropdownButtonFormField<String>(
                  value: vendors.any((v) => v['name'] == vendorName.text) ? vendorName.text : null,
                  isExpanded: true, hint: const Text('Choose from vendor list (or type below)'),
                  items: vendors.map((v) => DropdownMenuItem<String>(value: (v['name'] ?? '').toString(), child: Text((v['name'] ?? '').toString()))).toList(),
                  onChanged: (v) {
                    final vend = vendors.firstWhere((x) => x['name'] == v, orElse: () => {});
                    setS(() {
                      vendorName.text = v ?? '';
                      vendorGst.text = (vend['gst'] ?? '').toString();
                      vendorAddr.text = (vend['address'] ?? '').toString();
                      vendorPhone.text = (vend['phone'] ?? '').toString();
                      vendorContact.text = (vend['contactPerson'] ?? '').toString();
                    });
                  },
                  decoration: const InputDecoration(labelText: 'Choose Vendor', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: vendorName, decoration: const InputDecoration(labelText: 'Vendor Name *', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              Row(children: [
                Expanded(child: TextField(controller: vendorGst, decoration: const InputDecoration(labelText: 'Vendor GST', border: OutlineInputBorder()))),
                const SizedBox(width: 8),
                Expanded(child: TextField(controller: vendorPhone, decoration: const InputDecoration(labelText: 'Vendor Phone', border: OutlineInputBorder()))),
              ]),
              const SizedBox(height: 10),
              TextField(controller: vendorAddr, maxLines: 2, decoration: const InputDecoration(labelText: 'Vendor Address', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: vendorContact, decoration: const InputDecoration(labelText: 'Vendor Contact Person', border: OutlineInputBorder())),
              const SizedBox(height: 14),
              const Divider(),
              Row(children: [
                Expanded(child: DropdownButtonFormField<String>(value: status, isExpanded: true,
                  items: _statuses.where((s) => s != 'All').map((s) => DropdownMenuItem(value: s, child: Text(s))).toList(),
                  onChanged: (v) => setS(() => status = v!),
                  decoration: const InputDecoration(labelText: 'Status', border: OutlineInputBorder()))),
                const SizedBox(width: 8),
                Expanded(child: DropdownButtonFormField<String>(value: priority, isExpanded: true,
                  items: _priorities.map((s) => DropdownMenuItem(value: s, child: Text(s))).toList(),
                  onChanged: (v) => setS(() => priority = v!),
                  decoration: const InputDecoration(labelText: 'Priority', border: OutlineInputBorder()))),
              ]),
              const SizedBox(height: 10),
              TextField(controller: dept, decoration: const InputDecoration(labelText: 'Department', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: purpose, maxLines: 2, decoration: const InputDecoration(labelText: 'Purpose', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: deliveryDate, decoration: const InputDecoration(labelText: 'Delivery Date (yyyy-mm-dd)', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: approvedBy, decoration: const InputDecoration(labelText: 'Approved By', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: requestedBy, decoration: const InputDecoration(labelText: 'Requested By', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: paymentTerms, decoration: const InputDecoration(labelText: 'Payment Terms', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              if (budgetList.isNotEmpty)
                DropdownButtonFormField<String>(
                  value: budgetList.any((b) => b['description'] == budgetDesc.text) ? budgetDesc.text : null,
                  isExpanded: true, hint: const Text('Budget Link (optional)'),
                  items: budgetList.map((b) => DropdownMenuItem<String>(value: (b['description'] ?? '').toString(), child: Text((b['description'] ?? '').toString()))).toList(),
                  onChanged: (v) {
                    final b = budgetList.firstWhere((x) => x['description'] == v, orElse: () => {});
                    setS(() { budgetDesc.text = v ?? ''; selectedBudgetId = (b['id'] ?? '').toString(); });
                  },
                  decoration: const InputDecoration(labelText: 'Budget Link', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: notes, maxLines: 2, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: terms, maxLines: 2, decoration: const InputDecoration(labelText: 'Terms & Conditions', border: OutlineInputBorder())),
              const SizedBox(height: 14),
              const Divider(),
              Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                const Text('Items', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14)),
                TextButton.icon(onPressed: () => setS(() => items.add({'desc': '', 'qty': 1.0, 'rate': 0.0, 'gst': 18.0, 'amount': 0.0})),
                  icon: const Icon(Icons.add, size: 18), label: const Text('Item Add')),
              ]),
              ...items.asMap().entries.map((entry) {
                final idx = entry.key; final it = entry.value;
                final descC = TextEditingController(text: (it['desc'] ?? '').toString());
                final qtyC = TextEditingController(text: (it['qty'] ?? 1).toString());
                final rateC = TextEditingController(text: (it['rate'] ?? 0).toString());
                final gstC = TextEditingController(text: (it['gst'] ?? 18).toString());
                return Container(margin: const EdgeInsets.only(bottom: 8), padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(color: const Color(0xFFF8F5FF), borderRadius: BorderRadius.circular(8)),
                  child: Column(children: [
                    Row(children: [
                      Expanded(child: TextField(controller: descC, onChanged: (v) => it['desc'] = v,
                        decoration: const InputDecoration(labelText: 'Description', isDense: true, border: OutlineInputBorder()))),
                      IconButton(icon: const Icon(Icons.delete_outline, size: 18, color: Color(0xFFDC2626)),
                        onPressed: () => setS(() => items.removeAt(idx))),
                    ]),
                    Row(children: [
                      Expanded(child: TextField(controller: qtyC, keyboardType: TextInputType.number,
                        onChanged: (v) => setS(() { it['qty'] = double.tryParse(v) ?? 0; it['amount'] = ((it['qty']??0) as num) * ((it['rate']??0) as num) * (1 + ((it['gst']??0) as num)/100); }),
                        decoration: const InputDecoration(labelText: 'Qty', isDense: true, border: OutlineInputBorder()))),
                      const SizedBox(width: 6),
                      Expanded(child: TextField(controller: rateC, keyboardType: TextInputType.number,
                        onChanged: (v) => setS(() { it['rate'] = double.tryParse(v) ?? 0; it['amount'] = ((it['qty']??0) as num) * ((it['rate']??0) as num) * (1 + ((it['gst']??0) as num)/100); }),
                        decoration: const InputDecoration(labelText: 'Rate', isDense: true, border: OutlineInputBorder()))),
                      const SizedBox(width: 6),
                      Expanded(child: TextField(controller: gstC, keyboardType: TextInputType.number,
                        onChanged: (v) => setS(() { it['gst'] = double.tryParse(v) ?? 0; it['amount'] = ((it['qty']??0) as num) * ((it['rate']??0) as num) * (1 + ((it['gst']??0) as num)/100); }),
                        decoration: const InputDecoration(labelText: 'GST%', isDense: true, border: OutlineInputBorder()))),
                    ]),
                    Align(alignment: Alignment.centerRight,
                      child: Text('Amount: ₹${(((it['qty']??0) as num) * ((it['rate']??0) as num) * (1 + ((it['gst']??0) as num)/100)).toStringAsFixed(2)}',
                        style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w600))),
                  ]));
              }),
              const SizedBox(height: 8),
              Container(padding: const EdgeInsets.all(10), decoration: BoxDecoration(color: const Color(0xFFF0EBFF), borderRadius: BorderRadius.circular(8)),
                child: Column(children: [
                  Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [const Text('Subtotal', style: TextStyle(fontSize: 12)), Text('₹${sub.toStringAsFixed(2)}', style: const TextStyle(fontSize: 12))]),
                  Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [const Text('GST', style: TextStyle(fontSize: 12)), Text('₹${gst.toStringAsFixed(2)}', style: const TextStyle(fontSize: 12))]),
                  Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                    const Text('Grand Total', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13)),
                    Text('₹${(sub+gst).toStringAsFixed(2)}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: Color(0xFF059669))),
                  ]),
                ])),
              const SizedBox(height: 16),
              SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
                onPressed: saving ? null : () async {
                  if (vendorName.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Vendor Name required!'))); return; }
                  setS(() => saving = true);
                  try {
                    final cleanItems = items.where((it) => (it['desc'] ?? '').toString().trim().isNotEmpty).map((it) => {
                      'desc': it['desc'], 'qty': it['qty'], 'rate': it['rate'], 'gst': it['gst'],
                      'amount': ((it['qty']??0) as num) * ((it['rate']??0) as num) * (1 + ((it['gst']??0) as num)/100),
                    }).toList();
                    final s2 = cleanItems.fold(0.0, (s, it) => s + (((it['qty']??0) as num) * ((it['rate']??0) as num)).toDouble());
                    final g2 = cleanItems.fold(0.0, (s, it) => s + (((it['qty']??0) as num) * ((it['rate']??0) as num) * ((it['gst']??0) as num)/100).toDouble());
                    final data = {
                      if (isEdit) 'poNumber': existing!['poNumber'],
                      if (isEdit) 'poDate': existing!['poDate'],
                      if (isEdit) 'billDate': existing!['billDate'] ?? '',
                      if (isEdit) 'scanFilePath': existing!['scanFilePath'] ?? '',
                      if (isEdit) 'scanUploadDate': existing!['scanUploadDate'] ?? '',
                      'budgetId': selectedBudgetId,
                      'vendorName': vendorName.text.trim(), 'vendorGst': vendorGst.text.trim(),
                      'vendorAddr': vendorAddr.text.trim(), 'vendorPhone': vendorPhone.text.trim(),
                      'vendorContact': vendorContact.text.trim(),
                      'status': status, 'priority': priority, 'dept': dept.text.trim(), 'purpose': purpose.text.trim(),
                      'deliveryDate': deliveryDate.text.trim(), 'approvedBy': approvedBy.text.trim(),
                      'requestedBy': requestedBy.text.trim(), 'paymentTerms': paymentTerms.text.trim(),
                      'budgetDesc': budgetDesc.text.trim(), 'notes': notes.text.trim(), 'terms': terms.text.trim(),
                      'items': cleanItems, 'subtotal': s2, 'gstTotal': g2, 'grandTotal': s2 + g2,
                    };
                    await ApiService.savePO(data);
                    if (ctx.mounted) Navigator.pop(ctx);
                    if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                        SnackBar(content: Text(isEdit ? 'PO updated!' : 'PO created!'), backgroundColor: const Color(0xFF059669)));
                    _load();
                  } catch (e) {
                    setS(() => saving = false);
                    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
                  }
                },
                style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white),
                child: saving
                  ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : Text(isEdit ? 'Update' : 'Create PO', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
              )),
              const SizedBox(height: 8),
            ]),
          )),
        );
      }));
  }
}
