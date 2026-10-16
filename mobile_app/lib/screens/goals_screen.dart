import 'package:flutter/material.dart';
import '../services/api_service.dart';

// Mirrors AMPMTool desktop's weekly "IT Goals" tab — WeekNo is a sequential
// counter the server advances only via carry-forward (see HelpdeskDb's
// AutoCarryForwardGoals), never a calendar week-of-year. New goals ask the
// server for "the current week" via /api/goals/current-week; the ✎ carry
// forward button in the AppBar mirrors the desktop's manual button, and
// GetGoals() on the server auto-rolls an overdue week the same way the
// desktop does on every tab open.
class GoalsScreen extends StatefulWidget {
  const GoalsScreen({super.key});
  @override
  State<GoalsScreen> createState() => _GoalsScreenState();
}

class _GoalsScreenState extends State<GoalsScreen> {
  static const _categories = ['Infrastructure','Support','Project','Maintenance','Security','Other'];
  static const _priorities = ['Low','Medium','High','Urgent'];
  static const _statuses = ['Not Started','In Progress','On Hold','Completed','Cancelled'];

  List<Map<String, dynamic>> _goals = [];
  bool _loading = true;
  String? _error;
  int? _weekFilter;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final g = await ApiService.getGoals();
      setState(() { _goals = g; _loading = false; });
      if (_weekFilter == null && g.isNotEmpty) {
        _weekFilter = g.map((x) => (x['weekNo'] ?? 0) as int).reduce((a, b) => a > b ? a : b);
      }
    } catch (e) {
      setState(() { _loading = false; _error = e.toString(); });
    }
  }

  List<int> get _weeks {
    final s = _goals.map((g) => (g['weekNo'] ?? 0) as int).where((w) => w > 0).toSet().toList();
    s.sort((a, b) => b.compareTo(a));
    return s;
  }

  List<Map<String, dynamic>> get _filtered {
    if (_weekFilter == null) return _goals;
    return _goals.where((g) => (g['weekNo'] ?? 0) == _weekFilter).toList();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: const Text('IT Goals'),
        backgroundColor: const Color(0xFF0A192F),
        foregroundColor: Colors.white,
        actions: [
          IconButton(icon: const Icon(Icons.skip_next), tooltip: 'Carry forward incomplete goals to next week', onPressed: _carryForward),
          IconButton(icon: const Icon(Icons.refresh), onPressed: _load),
        ],
      ),
      body: Column(children: [
        if (_weeks.isNotEmpty)
          Container(color: Colors.white, padding: const EdgeInsets.all(12),
            child: SizedBox(height: 34, child: ListView(scrollDirection: Axis.horizontal,
              children: _weeks.map((w) => Padding(
                padding: const EdgeInsets.only(right: 8),
                child: FilterChip(
                  label: Text('Week $w', style: TextStyle(color: _weekFilter == w ? Colors.white : const Color(0xFF374151), fontWeight: FontWeight.w600, fontSize: 12)),
                  selected: _weekFilter == w,
                  onSelected: (_) => setState(() => _weekFilter = w),
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
              ? const Center(child: Text('No goals found', style: TextStyle(color: Colors.grey)))
              : ListView.builder(padding: const EdgeInsets.fromLTRB(12, 8, 12, 80),
                  itemCount: _filtered.length, itemBuilder: (_, i) => _card(_filtered[i]))),
      ]),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _showEdit(null),
        backgroundColor: const Color(0xFF059669),
        icon: const Icon(Icons.add, color: Colors.white),
        label: const Text('New Goal', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
      ),
    );
  }

  Future<void> _carryForward() async {
    final targetWeek = _weekFilter;
    final ok = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Carry Forward?'),
      content: Text('Incomplete goals from Week ${targetWeek ?? "(latest)"} will be copied to next week — status and remarks will be preserved.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        TextButton(onPressed: () => Navigator.pop(context, true), child: const Text('Carry Forward')),
      ]));
    if (ok != true) return;
    try {
      final res = await ApiService.carryForwardGoals(weekNo: targetWeek);
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text((res['message'] ?? 'Done').toString()), backgroundColor: const Color(0xFF059669)));
      _weekFilter = null;
      _load();
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  Color _statusColor(String s) {
    switch (s) {
      case 'Completed':   return const Color(0xFF059669);
      case 'In Progress': return const Color(0xFF2563EB);
      case 'On Hold':     return const Color(0xFFD97706);
      case 'Cancelled':   return const Color(0xFFDC2626);
      default:            return const Color(0xFF6B7280);
    }
  }

  Widget _card(Map<String, dynamic> g) {
    final status = (g['status'] ?? '').toString();
    final c = _statusColor(status);
    final progress = ((g['progress'] ?? 0) as num).toInt();
    return GestureDetector(
      onTap: () => _showDetail(g),
      child: Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: c, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)]),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text((g['title'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14))),
            if (g['isPending'] == true) ...[
              Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                decoration: BoxDecoration(color: const Color(0xFF7C3AED).withOpacity(.1), borderRadius: BorderRadius.circular(20)),
                child: Text('Wk ${g['pendingFromWeek'] ?? ''}', style: const TextStyle(color: Color(0xFF7C3AED), fontSize: 10, fontWeight: FontWeight.bold))),
              const SizedBox(width: 6),
            ],
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: c.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
              child: Text(status, style: TextStyle(color: c, fontSize: 10, fontWeight: FontWeight.bold))),
          ]),
          Text('${g['category'] ?? ''} · ${g['priority'] ?? ''} · ${g['department'] ?? ''}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
          const SizedBox(height: 8),
          ClipRRect(borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(value: progress / 100, minHeight: 6, backgroundColor: const Color(0xFFF3F4F6),
              valueColor: AlwaysStoppedAnimation(c))),
          const SizedBox(height: 4),
          Text('$progress%  ·  Target: ${g['targetDate'] ?? '—'}', style: const TextStyle(color: Colors.grey, fontSize: 11)),
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

  void _showDetail(Map<String, dynamic> g) {
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
          Text((g['title'] ?? '').toString(), style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
          const SizedBox(height: 12),
          _row('Week', g['weekNo']), _row('Category', g['category']), _row('Priority', g['priority']),
          _row('Department', g['department']), _row('Requested By', g['requestedBy']), _row('Assigned To', g['assignedTo']),
          _row('Start Date', g['startDate']), _row('Target Date', g['targetDate']), _row('Completed On', g['completedOn']),
          _row('Status', g['status']), _row('Progress', '${g['progress'] ?? 0}%'),
          _row('Ticket Ref', g['ticketRef']), _row('Remarks', g['remarks']),
          if (g['isPending'] == true) _row('Carried From', 'Week ${g['pendingFromWeek'] ?? ''}'),
          const SizedBox(height: 20),
          Row(children: [
            Expanded(child: OutlinedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showEdit(g); },
              icon: const Icon(Icons.edit), label: const Text('Edit'))),
            const SizedBox(width: 10),
            Expanded(child: ElevatedButton.icon(
              onPressed: () { Navigator.pop(ctx); _showUpdateStatus(g); },
              icon: const Icon(Icons.update), label: const Text('Update Status'),
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white))),
          ]),
        ])),
      ),
    );
  }

  void _showUpdateStatus(Map<String, dynamic> g) {
    String status = _statuses.contains(g['status']) ? g['status'] : _statuses[0];
    double progress = ((g['progress'] ?? 0) as num).toDouble();
    final remarks = TextEditingController(text: (g['remarks'] ?? '').toString());
    bool saving = false;
    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text('Status Update — ${g['title']}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(value: status, isExpanded: true,
            items: _statuses.map((s) => DropdownMenuItem(value: s, child: Text(s))).toList(),
            onChanged: (v) => setS(() { status = v!; if (status == 'Completed') progress = 100; }),
            decoration: const InputDecoration(labelText: 'Status', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          Text('Progress: ${progress.round()}%', style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
          Slider(value: progress, min: 0, max: 100, divisions: 20,
            label: '${progress.round()}%', onChanged: (v) => setS(() => progress = v)),
          const SizedBox(height: 6),
          TextField(controller: remarks, maxLines: 2, decoration: const InputDecoration(labelText: 'Remarks', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              setS(() => saving = true);
              try {
                final data = Map<String, dynamic>.from(g);
                data['status'] = status;
                data['progress'] = progress.round();
                data['remarks'] = remarks.text.trim();
                if (status == 'Completed' && (data['completedOn'] ?? '').toString().isEmpty) {
                  data['completedOn'] = DateTime.now().toString().split(' ')[0];
                }
                await ApiService.saveGoal(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Goal updated!'), backgroundColor: Color(0xFF059669)));
                _load();
              } catch (e) {
                setS(() => saving = false);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
              }
            },
            style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white),
            child: saving
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Save Update', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
          )),
          const SizedBox(height: 8),
        ]),
      )));
  }

  Future<void> _showEdit(Map<String, dynamic>? existing) async {
    final isEdit = existing != null;

    // Week is never hand-typed — it's tracked server-side (same rule as the
    // desktop) and only advances via carry-forward, so a new goal always
    // asks the server what "this week" currently is.
    int weekNo;
    String weekStart;
    String weekEnd;
    if (isEdit) {
      final e = existing!;
      weekNo = (e['weekNo'] ?? 1) as int;
      weekStart = (e['weekStart'] ?? '').toString();
      weekEnd = (e['weekEnd'] ?? '').toString();
    } else {
      try {
        final info = await ApiService.getCurrentWeekInfo();
        weekNo = (info['weekNo'] ?? 1) as int;
        weekStart = (info['weekStart'] ?? '').toString();
        weekEnd = (info['weekEnd'] ?? '').toString();
      } catch (e) {
        if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
        return;
      }
    }
    if (!mounted) return;

    final title = TextEditingController(text: (existing?['title'] ?? '').toString());
    final department = TextEditingController(text: (existing?['department'] ?? '').toString());
    final requestedBy = TextEditingController(text: (existing?['requestedBy'] ?? '').toString());
    final assignedTo = TextEditingController(text: (existing?['assignedTo'] ?? 'Sandy').toString());
    final startDate = TextEditingController(text: (existing?['startDate'] ?? DateTime.now().toString().split(' ')[0]).toString());
    final targetDate = TextEditingController(text: (existing?['targetDate'] ?? weekEnd).toString());
    final ticketRef = TextEditingController(text: (existing?['ticketRef'] ?? '').toString());
    final remarks = TextEditingController(text: (existing?['remarks'] ?? '').toString());
    String category = _categories.contains(existing?['category']) ? existing!['category'] : _categories[0];
    String priority = _priorities.contains(existing?['priority']) ? existing!['priority'] : 'Medium';
    bool saving = false;

    showModalBottomSheet(context: context, isScrollControlled: true, backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (ctx) => StatefulBuilder(builder: (ctx, setS) => Padding(
        padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
        child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(isEdit ? 'Edit Goal' : 'New Goal', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
            IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
          ]),
          const SizedBox(height: 10),
          Container(width: double.infinity, padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(color: const Color(0xFF0A192F), borderRadius: BorderRadius.circular(8)),
            child: Text('Week $weekNo   |   $weekStart  →  $weekEnd',
              style: const TextStyle(color: Colors.white, fontSize: 12, fontWeight: FontWeight.bold))),
          const SizedBox(height: 12),
          TextField(controller: title, decoration: const InputDecoration(labelText: 'Goal Title *', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          Row(children: [
            Expanded(child: DropdownButtonFormField<String>(value: category, isExpanded: true,
              items: _categories.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => category = v!),
              decoration: const InputDecoration(labelText: 'Category', border: OutlineInputBorder()))),
            const SizedBox(width: 8),
            Expanded(child: DropdownButtonFormField<String>(value: priority, isExpanded: true,
              items: _priorities.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setS(() => priority = v!),
              decoration: const InputDecoration(labelText: 'Priority', border: OutlineInputBorder()))),
          ]),
          const SizedBox(height: 10),
          TextField(controller: department, decoration: const InputDecoration(labelText: 'Department', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: requestedBy, decoration: const InputDecoration(labelText: 'Requested By', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: assignedTo, decoration: const InputDecoration(labelText: 'Assigned To', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: startDate, decoration: const InputDecoration(labelText: 'Start Date', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: targetDate, decoration: const InputDecoration(labelText: 'Target Date (yyyy-mm-dd)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: ticketRef, decoration: const InputDecoration(labelText: 'Ticket Ref (optional)', border: OutlineInputBorder())),
          const SizedBox(height: 10),
          TextField(controller: remarks, maxLines: 2, decoration: const InputDecoration(labelText: 'Remarks', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          SizedBox(width: double.infinity, height: 48, child: ElevatedButton(
            onPressed: saving ? null : () async {
              if (title.text.trim().isEmpty) { ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Title required!'))); return; }
              setS(() => saving = true);
              try {
                final data = {
                  if (isEdit) 'id': existing!['id'],
                  if (isEdit) 'status': existing!['status'] ?? 'Not Started',
                  if (isEdit) 'progress': existing!['progress'] ?? 0,
                  if (isEdit) 'completedOn': existing!['completedOn'] ?? '',
                  if (isEdit) 'sno': existing!['sno'] ?? 0,
                  if (isEdit) 'isPending': existing!['isPending'] ?? false,
                  if (isEdit) 'pendingFromWeek': existing!['pendingFromWeek'] ?? 0,
                  'weekNo': weekNo, 'weekStart': weekStart, 'weekEnd': weekEnd,
                  'title': title.text.trim(), 'category': category, 'priority': priority,
                  'department': department.text.trim(), 'requestedBy': requestedBy.text.trim(),
                  'assignedTo': assignedTo.text.trim(), 'startDate': startDate.text.trim(),
                  'targetDate': targetDate.text.trim(), 'ticketRef': ticketRef.text.trim(),
                  'remarks': remarks.text.trim(), 'approval': 'Approved',
                };
                await ApiService.saveGoal(data);
                if (ctx.mounted) Navigator.pop(ctx);
                if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text(isEdit ? 'Goal updated!' : 'Goal added!'), backgroundColor: const Color(0xFF059669)));
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
