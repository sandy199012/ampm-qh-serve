import 'package:flutter/material.dart';
import '../services/api_service.dart';

class AssetStockScreen extends StatefulWidget {
  const AssetStockScreen({super.key});
  @override
  State<AssetStockScreen> createState() => _AssetStockScreenState();
}

class _AssetStockScreenState extends State<AssetStockScreen> {
  static const _assetTypes = ['Laptop','Desktop','Monitor','Printer','Scanner','UPS','Switch',
    'Router','Phone','Tablet','Keyboard','Mouse','Webcam','Headset','Projector','Other'];
  static const _statuses = ['All','In Stock','Assigned','Under Repair','Retired','Lost'];
  static const _conditions = ['New','Good','Fair','Under Repair','Scrap'];

  List<Map<String, dynamic>> _assets = [];
  List<Map<String, dynamic>> _filtered = [];
  bool _loading = true;
  String? _error;
  final _search = TextEditingController();
  String _status = 'All';

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final a = await ApiService.getAssets();
      setState(() { _assets = a; _loading = false; });
      _applyFilter();
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  void _applyFilter() {
    var list = _assets.toList();
    if (_status != 'All') list = list.where((a) => (a['status'] ?? '') == _status).toList();
    if (_search.text.isNotEmpty) {
      final q = _search.text.toLowerCase();
      list = list.where((a) =>
        (a['assetTag'] ?? '').toString().toLowerCase().contains(q) ||
        (a['assetType'] ?? '').toString().toLowerCase().contains(q) ||
        (a['brand'] ?? '').toString().toLowerCase().contains(q) ||
        (a['model'] ?? '').toString().toLowerCase().contains(q) ||
        (a['serial'] ?? '').toString().toLowerCase().contains(q) ||
        (a['assignedToName'] ?? '').toString().toLowerCase().contains(q)).toList();
    }
    setState(() => _filtered = list);
  }

  Color _statusColor(String s) {
    switch (s) {
      case 'Assigned':     return const Color(0xFF2563EB);
      case 'Under Repair': return const Color(0xFFD97706);
      case 'Retired':      return const Color(0xFF6B7280);
      case 'Lost':         return const Color(0xFFDC2626);
      default:             return const Color(0xFF059669); // In Stock
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: const Text('Asset Stock'),
        backgroundColor: const Color(0xFF0A192F),
        foregroundColor: Colors.white,
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: Column(children: [
        Container(
          color: Colors.white,
          padding: const EdgeInsets.all(12),
          child: Column(children: [
            TextField(
              controller: _search,
              onChanged: (_) => _applyFilter(),
              decoration: InputDecoration(
                hintText: 'Tag, type, brand, serial, employee...',
                prefixIcon: const Icon(Icons.search, color: Colors.grey),
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
                contentPadding: const EdgeInsets.symmetric(vertical: 8),
                isDense: true,
              ),
            ),
            const SizedBox(height: 8),
            SizedBox(height: 34, child: ListView(scrollDirection: Axis.horizontal,
              children: _statuses.map((s) => Padding(
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
              )).toList()),
            ),
          ]),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: Row(children: [
            Text(_loading ? '...' : '${_filtered.length} assets', style: const TextStyle(color: Colors.grey, fontSize: 12)),
          ]),
        ),
        Expanded(
          child: _loading
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
                ? const Center(child: Text('No assets found', style: TextStyle(color: Colors.grey)))
                : ListView.builder(
                    padding: const EdgeInsets.fromLTRB(12, 0, 12, 80),
                    itemCount: _filtered.length,
                    itemBuilder: (_, i) => _card(_filtered[i]),
                  ),
        ),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _showAddAsset,
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New Asset', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Widget _card(Map<String, dynamic> a) {
    final status = (a['status'] ?? '').toString();
    final c = _statusColor(status);
    final assignedName = (a['assignedToName'] ?? '').toString();
    return GestureDetector(
      onTap: () => _showDetail(a),
      child: Container(
        margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: c, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Text((a['assetTag'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, color: Color(0xFF2563EB), fontSize: 12)),
            const SizedBox(width: 8),
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: c.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text(status, style: TextStyle(color: c, fontSize: 10, fontWeight: FontWeight.bold))),
            const Spacer(),
            Text((a['assetType'] ?? '').toString(), style: const TextStyle(color: Colors.grey, fontSize: 11)),
          ]),
          const SizedBox(height: 6),
          Text('${a['brand'] ?? ''} ${a['model'] ?? ''}'.trim(),
              style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
          if ((a['serial'] ?? '').toString().isNotEmpty)
            Text('SN: ${a['serial']}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
          if (assignedName.isNotEmpty) ...[
            const SizedBox(height: 4),
            Row(children: [
              const Icon(Icons.person, size: 12, color: Colors.grey),
              const SizedBox(width: 4),
              Text('$assignedName · ${a['assignedToDept'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
            ]),
          ],
        ]),
      ),
    );
  }

  void _showDetail(Map<String, dynamic> a) {
    showModalBottomSheet(
      context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => Padding(
        padding: EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.of(ctx).viewInsets.bottom + 20),
        child: SingleChildScrollView(
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              const Spacer(),
              IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
            ]),
            Text((a['assetTag'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
            const SizedBox(height: 4),
            Text('${a['assetType']} · ${a['brand']} ${a['model']}', style: const TextStyle(color: Colors.grey, fontSize: 13)),
            const SizedBox(height: 16),
            _row('Serial', a['serial']), _row('Processor', a['processor']), _row('RAM', a['ram']),
            _row('Storage', a['storage']), _row('OS', a['os']), _row('Condition', a['condition']),
            _row('Status', a['status']), _row('Location', a['location']),
            _row('Purchase Date', a['purchaseDate']),
            _row('Purchase Cost', (a['purchaseCost'] != null && a['purchaseCost'] != 0) ? '₹${a['purchaseCost']}' : ''),
            _row('Vendor', a['vendor']), _row('Warranty', a['warranty']),
            if ((a['assignedToName'] ?? '').toString().isNotEmpty) ...[
              const Divider(height: 24),
              _row('Assigned To', a['assignedToName']), _row('Department', a['assignedToDept']),
              _row('Assigned Date', a['assignedDate']),
            ],
            if ((a['notes'] ?? '').toString().isNotEmpty) _row('Notes', a['notes']),
            const SizedBox(height: 20),
            Row(children: [
              if ((a['status'] ?? '') == 'Assigned')
                Expanded(child: OutlinedButton.icon(
                  onPressed: () { Navigator.pop(ctx); _unassign(a); },
                  icon: const Icon(Icons.person_remove), label: const Text('Unassign'),
                  style: OutlinedButton.styleFrom(foregroundColor: const Color(0xFFDC2626)),
                ))
              else
                Expanded(child: ElevatedButton.icon(
                  onPressed: () { Navigator.pop(ctx); _showAssign(a); },
                  icon: const Icon(Icons.person_add), label: const Text('Assign'),
                  style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white),
                )),
            ]),
          ]),
        ),
      ),
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

  Future<void> _unassign(Map<String, dynamic> a) async {
    try {
      await ApiService.unassignAsset(a['id'].toString());
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Asset unassigned'), backgroundColor: Color(0xFF059669)));
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  Future<void> _showAssign(Map<String, dynamic> a) async {
    List<Map<String, dynamic>> emps = [];
    try { emps = await ApiService.getEmployeesFull(); } catch (_) {}
    if (!mounted) return;
    final search = TextEditingController();
    List<Map<String, dynamic>> filtered = emps;
    await showModalBottomSheet(
      context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) {
        return Padding(
          padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
          child: SizedBox(
            height: MediaQuery.of(ctx).size.height * .7,
            child: Column(children: [
              Row(children: [
                Expanded(child: Text('Assign — ${a['assetTag']}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
                IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
              ]),
              const SizedBox(height: 12),
              TextField(
                controller: search,
                onChanged: (v) => setS(() {
                  final q = v.toLowerCase();
                  filtered = emps.where((e) =>
                    (e['name'] ?? '').toString().toLowerCase().contains(q) ||
                    (e['empId'] ?? '').toString().toLowerCase().contains(q) ||
                    (e['dept'] ?? '').toString().toLowerCase().contains(q)).toList();
                }),
                decoration: InputDecoration(hintText: 'Search employee...',
                  prefixIcon: const Icon(Icons.search),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)), isDense: true),
              ),
              const SizedBox(height: 8),
              Expanded(child: ListView.builder(
                itemCount: filtered.length,
                itemBuilder: (_, i) {
                  final e = filtered[i];
                  return ListTile(
                    title: Text((e['name'] ?? '').toString()),
                    subtitle: Text('${e['empId']} · ${e['dept']}'),
                    onTap: () async {
                      Navigator.pop(ctx);
                      try {
                        await ApiService.assignAsset(a['id'].toString(), (e['empId'] ?? '').toString());
                        if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                            SnackBar(content: Text('${a['assetTag']} assigned to ${e['name']}'), backgroundColor: const Color(0xFF059669)));
                        _load();
                      } catch (err) {
                        if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $err'), backgroundColor: Colors.red));
                      }
                    },
                  );
                },
              )),
            ]),
          ),
        );
      }),
    );
  }

  void _showAddAsset() {
    final tag = TextEditingController();
    final brand = TextEditingController();
    final model = TextEditingController();
    final serial = TextEditingController();
    final processor = TextEditingController();
    final ram = TextEditingController();
    final storage = TextEditingController();
    final os = TextEditingController();
    final vendor = TextEditingController();
    final invoice = TextEditingController();
    final warranty = TextEditingController();
    final location = TextEditingController();
    final cost = TextEditingController();
    final notes = TextEditingController();
    String type = _assetTypes[0];
    String condition = _conditions[1]; // Good
    bool saving = false;

    showModalBottomSheet(
      context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) {
        return Padding(
          padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
          child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                const Expanded(child: Text('New Asset', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
                IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
              ]),
              const SizedBox(height: 4),
              const Text('Asset Tag will be auto-generated (AMPM-XXXX)', style: TextStyle(color: Colors.grey, fontSize: 11)),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(value: type, isExpanded: true,
                items: _assetTypes.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
                onChanged: (v) => setS(() => type = v!),
                decoration: const InputDecoration(labelText: 'Asset Type', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: brand, decoration: const InputDecoration(labelText: 'Brand', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: model, decoration: const InputDecoration(labelText: 'Model', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: serial, decoration: const InputDecoration(labelText: 'Serial Number', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: processor, decoration: const InputDecoration(labelText: 'Processor', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: ram, decoration: const InputDecoration(labelText: 'RAM', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: storage, decoration: const InputDecoration(labelText: 'Storage', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: os, decoration: const InputDecoration(labelText: 'OS', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              DropdownButtonFormField<String>(value: condition, isExpanded: true,
                items: _conditions.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
                onChanged: (v) => setS(() => condition = v!),
                decoration: const InputDecoration(labelText: 'Condition', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: location, decoration: const InputDecoration(labelText: 'Location', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: cost, keyboardType: TextInputType.number,
                  decoration: const InputDecoration(labelText: 'Purchase Cost (₹)', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: vendor, decoration: const InputDecoration(labelText: 'Vendor', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: invoice, decoration: const InputDecoration(labelText: 'Invoice No', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: warranty, decoration: const InputDecoration(labelText: 'Warranty', border: OutlineInputBorder())),
              const SizedBox(height: 10),
              TextField(controller: notes, maxLines: 2, decoration: const InputDecoration(labelText: 'Notes', border: OutlineInputBorder())),
              const SizedBox(height: 16),
              SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
                onPressed: saving ? null : () async {
                  setS(() => saving = true);
                  try {
                    await ApiService.saveAsset({
                      'assetType': type, 'brand': brand.text.trim(), 'model': model.text.trim(),
                      'serial': serial.text.trim(), 'processor': processor.text.trim(),
                      'ram': ram.text.trim(), 'storage': storage.text.trim(), 'os': os.text.trim(),
                      'condition': condition, 'location': location.text.trim(),
                      'purchaseCost': double.tryParse(cost.text.trim()) ?? 0,
                      'vendor': vendor.text.trim(), 'invoiceNo': invoice.text.trim(),
                      'warranty': warranty.text.trim(), 'notes': notes.text.trim(),
                    });
                    if (ctx.mounted) Navigator.pop(ctx);
                    if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(content: Text('Asset added!'), backgroundColor: Color(0xFF059669)));
                    _load();
                  } catch (e) {
                    setS(() => saving = false);
                    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
                  }
                },
                style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF059669), foregroundColor: Colors.white),
                child: saving
                  ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Text('Add Asset', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
              )),
              const SizedBox(height: 8),
            ]),
          ),
        );
      }),
    );
  }
}
