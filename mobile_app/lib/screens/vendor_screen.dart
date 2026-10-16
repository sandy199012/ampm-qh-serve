import 'package:flutter/material.dart';
import '../services/api_service.dart';

class VendorScreen extends StatefulWidget {
  const VendorScreen({super.key});
  @override
  State<VendorScreen> createState() => _VendorScreenState();
}

class _VendorScreenState extends State<VendorScreen> {
  List<Map<String, dynamic>> _vendors = [];
  bool _loading = true;
  String? _error;
  final _search = TextEditingController();

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final v = await ApiService.getVendors();
      setState(() { _vendors = v; _loading = false; });
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  List<Map<String, dynamic>> get _filtered {
    if (_search.text.isEmpty) return _vendors;
    final q = _search.text.toLowerCase();
    return _vendors.where((v) =>
      (v['name'] ?? '').toString().toLowerCase().contains(q) ||
      (v['category'] ?? '').toString().toLowerCase().contains(q) ||
      (v['contactPerson'] ?? '').toString().toLowerCase().contains(q)).toList();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF5F2FF),
      appBar: AppBar(
        title: const Text('Vendors'),
        backgroundColor: const Color(0xFF130A33),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        Container(color: Colors.white, padding: const EdgeInsets.all(12),
          child: TextField(controller: _search, onChanged: (_) => setState(() {}),
            decoration: InputDecoration(hintText: 'Search vendor...',
              prefixIcon: const Icon(Icons.search, color: Colors.grey),
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
              contentPadding: const EdgeInsets.symmetric(vertical: 8), isDense: true))),
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
              ? const Center(child: Text('No vendors found', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _filtered.length, itemBuilder: (_, i) => _card(_filtered[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New Vendor', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _card(Map<String, dynamic> v) {
    return GestureDetector(
      onTap: () => _showDetail(v),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: const Border(left: BorderSide(color: Color(0xFF6D4AFF), width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((v['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            if ((v['category'] ?? '').toString().isNotEmpty)
              Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                decoration: BoxDecoration(color: const Color(0xFF6D4AFF).withOpacity(.1), borderRadius: BorderRadius.circular(20)),
                child: Text((v['category'] ?? '').toString(), style: const TextStyle(color: Color(0xFF6D4AFF), fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          if ((v['contactPerson'] ?? '').toString().isNotEmpty)
            Text('${v['contactPerson']} · ${v['phone'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
          if ((v['gst'] ?? '').toString().isNotEmpty)
            Text('GST: ${v['gst']}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
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

  void _showDetail(Map<String, dynamic> v) {
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
          Text((v['name'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          const SizedBox(height: 12),
          _row('GST No', v['gst']), _row('Address', v['address']), _row('Phone', v['phone']),
          _row('Email', v['email']), _row('Contact Person', v['contactPerson']),
          _row('Payment Terms', v['paymentTerms']), _row('Category', v['category']), _row('Notes', v['notes']),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEdit(v); },
              icon: const Icon(Icons.edit), label: const Text('Edit'))),
            const SizedBox(width: 10),
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _confirmDelete(v); },
              icon: const Icon(Icons.delete_outline, color: Color(0xFFDC2626)),
              label: const Text('Delete', style: TextStyle(color: Color(0xFFDC2626))),
              style: OutlinedButton.styleFrom(side: const BorderSide(color: Color(0xFFDC2626))))),
          ]),
        ])),
      ),
    );
  }

  Future<void> _confirmDelete(Map<String, dynamic> v) async {
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Delete this vendor?'),
      content: Text('${v['name']} will be permanently deleted.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        TextButton(onPressed: () => Navigator.pop(context, true),
          child: const Text('Delete', style: TextStyle(color: Colors.red, fontWeight: FontWeight.bold))),
      ]));
    if (ok != true) return;
    try {
      await ApiService.deleteVendor(v['vendorId'].toString());
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Vendor deleted'), backgroundColor: Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  void _showEdit(Map<String, dynamic>? existing) {
    final isEdit = existing != null;
    final name = TextEditingController(text: (existing?['name'] ?? '').toString());
    final gst = TextEditingController(text: (existing?['gst'] ?? '').toString());
    final address = TextEditingController(text: (existing?['address'] ?? '').toString());
    final phone = TextEditingController(text: (existing?['phone'] ?? '').toString());
    final email = TextEditingController(text: (existing?['email'] ?? '').toString());
    final contact = TextEditingController(text: (existing?['contactPerson'] ?? '').toString());
    final terms = TextEditingController(text: (existing?['paymentTerms'] ?? '').toString());
    final category = TextEditingController(text: (existing?['category'] ?? '').toString());
    final notes = TextEditingController(text: (existing?['notes'] ?? '').toString());
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(isEdit ? 'Edit Vendor' : 'New Vendor', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          TextField(controller: name, decoration: const InputDecoration(labelText: 'Vendor Name *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: gst, decoration: const InputDecoration(labelText: 'GST No', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: address, maxLines: 2, decoration: const InputDecoration(labelText: 'Address', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: phone, decoration: const InputDecoration(labelText: 'Phone', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: email, decoration: const InputDecoration(labelText: 'Email', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: contact, decoration: const InputDecoration(labelText: 'Contact Person', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: terms, decoration: const InputDecoration(labelText: 'Payment Terms', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: category, decoration: const InputDecoration(labelText: 'Category', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: notes, maxLines: 2, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (name.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Name required!'))); return; }
              setS(() => saving = true);
              try {
                final data = {
                  if (isEdit) 'vendorId': existing!['vendorId'],
                  'name': name.text.trim(), 'gst': gst.text.trim(), 'address': address.text.trim(),
                  'phone': phone.text.trim(), 'email': email.text.trim(), 'contactPerson': contact.text.trim(),
                  'paymentTerms': terms.text.trim(), 'category': category.text.trim(), 'notes': notes.text.trim(),
                };
                await ApiService.saveVendor(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(isEdit ? 'Vendor updated!' : 'Vendor added!'), backgroundColor: const Color(0xFF059669)));
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
