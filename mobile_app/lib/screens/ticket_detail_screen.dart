import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../models/ticket.dart';
import '../services/api_service.dart';

class TicketDetailScreen extends StatefulWidget {
  final Ticket ticket;
  const TicketDetailScreen({super.key, required this.ticket});
  @override
  State<TicketDetailScreen> createState() => _TicketDetailScreenState();
}

class _TicketDetailScreenState extends State<TicketDetailScreen> {
  late Ticket _ticket;
  bool _updating = false;
  // Regular employees can only raise/view tickets — the server rejects
  // status updates and deletes from non-admin accounts, so hide those
  // controls for them instead of showing an action that will just fail.
  bool _isAdmin = false;

  // Employee feedback form state (non-admin, once Resolved/Closed).
  late String _fbStatus;
  late final TextEditingController _fbComment;
  bool _fbSubmitting = false;

  @override
  void initState() {
    super.initState();
    _ticket = widget.ticket;
    _fbStatus = _ticket.empFeedbackStatus.isNotEmpty ? _ticket.empFeedbackStatus : 'Resolved';
    _fbComment = TextEditingController(text: _ticket.empFeedback);
    _loadRole();
  }

  @override
  void dispose() { _fbComment.dispose(); super.dispose(); }

  Future<void> _loadRole() async {
    final isAdmin = await ApiService.getSavedIsAdmin();
    if (mounted) setState(() => _isAdmin = isAdmin);
  }

  // Combined date+time picker used for the Expected Resolution (ETA) field
  // in the admin Update Ticket sheet — same info the web "Set/Update ETA"
  // datetime-local picker collects.
  Future<DateTime?> _pickDateTime(BuildContext ctx, DateTime? initial) async {
    final now = DateTime.now();
    final date = await showDatePicker(
      context: ctx,
      initialDate: initial ?? now,
      firstDate: DateTime(now.year - 1),
      lastDate: DateTime(now.year + 3),
    );
    if (date == null) return null;
    if (!ctx.mounted) return null;
    final time = await showTimePicker(
      context: ctx,
      initialTime: initial != null ? TimeOfDay.fromDateTime(initial) : TimeOfDay.now(),
    );
    if (time == null) return null;
    return DateTime(date.year, date.month, date.day, time.hour, time.minute);
  }

  Future<void> _submitFeedback() async {
    setState(() => _fbSubmitting = true);
    try {
      await ApiService.submitFeedback(_ticket.ticketId, _fbStatus, _fbComment.text.trim());
      final updated = await ApiService.getTicket(_ticket.ticketId);
      if (mounted) setState(() { _ticket = updated; _fbSubmitting = false; });
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Feedback submitted — thank you!'), backgroundColor: Color(0xFF059669)));
    } catch (e) {
      setState(() => _fbSubmitting = false);
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  Future<void> _showUpdateDialog() async {
    String status    = _ticket.status;
    String assignedTo = _ticket.assignedTo;
    String resolution = _ticket.resolution;
    String remarks   = _ticket.remarks;
    DateTime? eta = DateTime.tryParse(_ticket.expectedResolutionRaw);

    await showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => StatefulBuilder(builder: (ctx, setS) {
        return Padding(
          padding: EdgeInsets.fromLTRB(16, 16, 16, MediaQuery.of(ctx).viewInsets.bottom + 16),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              const Expanded(child: Text('Update Ticket', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 18))),
              IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(ctx), padding: EdgeInsets.zero, constraints: const BoxConstraints()),
            ]),
            const SizedBox(height: 16),
            DropdownButtonFormField<String>(
              value: status,
              items: ['Open','Acknowledged','In Progress','On Hold','Resolved','Closed']
                  .map((s) => DropdownMenuItem(value: s, child: Text(s))).toList(),
              onChanged: (v) => setS(() => status = v!),
              decoration: const InputDecoration(labelText: 'Status', border: OutlineInputBorder()),
            ),
            const SizedBox(height: 12),
            TextFormField(
              initialValue: assignedTo,
              onChanged: (v) => assignedTo = v,
              decoration: const InputDecoration(labelText: 'Assigned To', border: OutlineInputBorder()),
            ),
            const SizedBox(height: 12),
            TextFormField(
              initialValue: resolution,
              onChanged: (v) => resolution = v,
              maxLines: 3,
              decoration: const InputDecoration(labelText: 'Resolution / Action Taken', border: OutlineInputBorder()),
            ),
            const SizedBox(height: 12),
            TextFormField(
              initialValue: remarks,
              onChanged: (v) => remarks = v,
              decoration: const InputDecoration(labelText: 'Remarks', border: OutlineInputBorder()),
            ),
            const SizedBox(height: 12),
            // Expected Resolution Timeline (ETA) — same idea as the web
            // Acknowledge/Set ETA date+time picker, just via native pickers.
            const Text('Expected Resolution (ETA)',
                style: TextStyle(fontWeight: FontWeight.w600, fontSize: 12, color: Color(0xFF374151))),
            const SizedBox(height: 6),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
              decoration: BoxDecoration(border: Border.all(color: const Color(0xFFBDBDBD)), borderRadius: BorderRadius.circular(8)),
              child: Row(children: [
                Expanded(child: Text(
                  eta != null ? DateFormat('dd-MMM-yyyy hh:mm a').format(eta!) : 'Not set',
                  style: TextStyle(fontSize: 13, color: eta != null ? Colors.black87 : Colors.grey))),
                TextButton.icon(
                  onPressed: () async {
                    final picked = await _pickDateTime(ctx, eta);
                    if (picked != null) setS(() => eta = picked);
                  },
                  icon: const Icon(Icons.event, size: 18),
                  label: const Text('Set'),
                ),
                if (eta != null)
                  IconButton(icon: const Icon(Icons.close, size: 18, color: Colors.grey),
                      onPressed: () => setS(() => eta = null), tooltip: 'Clear'),
              ]),
            ),
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              height: 48,
              child: ElevatedButton(
                onPressed: () async {
                  Navigator.pop(ctx);
                  setState(() => _updating = true);
                  try {
                    await ApiService.updateTicket(_ticket.ticketId, {
                      'status': status, 'assignedTo': assignedTo,
                      'resolution': resolution, 'remarks': remarks,
                      'expectedResolution': eta != null ? DateFormat("yyyy-MM-dd'T'HH:mm").format(eta!) : '',
                    });
                    final updated = await ApiService.getTicket(_ticket.ticketId);
                    setState(() { _ticket = updated; _updating = false; });
                    if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(content: Text('Ticket updated!'), backgroundColor: Color(0xFF059669)));
                  } catch (e) {
                    setState(() => _updating = false);
                    if (mounted) ScaffoldMessenger.of(context).showSnackBar(
                        SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
                  }
                },
                style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white),
                child: const Text('Save Update', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
              ),
            ),
          ]),
        );
      }),
    );
  }

  Future<void> _confirmDelete() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Delete this ticket?'),
        content: Text('${_ticket.ticketNo} will be permanently deleted — this cannot be undone.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          TextButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete', style: TextStyle(color: Colors.red, fontWeight: FontWeight.bold)),
          ),
        ],
      ),
    );
    if (ok != true) return;
    setState(() => _updating = true);
    try {
      await ApiService.deleteTicket(_ticket.ticketId);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      setState(() => _updating = false);
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Error: $e'), backgroundColor: Colors.red));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF0F4F8),
      appBar: AppBar(
        title: Text(_ticket.ticketNo),
        backgroundColor: const Color(0xFF0A192F),
        foregroundColor: Colors.white,
        actions: [
          if (_updating)
            const Padding(padding: EdgeInsets.all(16), child: SizedBox(width: 20, height: 20,
                child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2)))
          else if (_isAdmin) ...[
            IconButton(icon: const Icon(Icons.edit_note), onPressed: _showUpdateDialog, tooltip: 'Update Ticket'),
            IconButton(icon: const Icon(Icons.delete_outline, color: Colors.redAccent),
                onPressed: _confirmDelete, tooltip: 'Delete Ticket'),
          ],
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          // Status banner
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            decoration: BoxDecoration(
              color: _ticket.statusColor.withOpacity(.1),
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: _ticket.statusColor.withOpacity(.3)),
            ),
            child: Row(children: [
              Icon(Icons.circle, color: _ticket.statusColor, size: 10),
              const SizedBox(width: 8),
              Text(_ticket.status, style: TextStyle(color: _ticket.statusColor, fontWeight: FontWeight.bold, fontSize: 15)),
              const Spacer(),
              Container(padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(color: _ticket.priorityColor.withOpacity(.1), borderRadius: BorderRadius.circular(20)),
                child: Text(_ticket.priority, style: TextStyle(color: _ticket.priorityColor, fontSize: 12, fontWeight: FontWeight.bold))),
            ]),
          ),
          const SizedBox(height: 16),

          _section('Employee', [
            _row('Name', _ticket.empName),
            _row('Emp ID', _ticket.empId),
            _row('Department', _ticket.empDept),
            _row('Designation', _ticket.empDesig),
            _row('Phone', _ticket.empMobile),
            _row('Location', _ticket.empHod),
          ]),
          const SizedBox(height: 12),

          _section('Issue', [
            _row('Issue Type', _ticket.issueType),
            _row('Category', _ticket.category),
            _row('Date Raised', _ticket.dateRaised),
            _row('Expected Resolution (ETA)', _ticket.expectedResolution),
          ]),
          if (_ticket.expectedResolution.isNotEmpty && _ticket.status != 'Resolved' && _ticket.status != 'Closed') ...[
            const SizedBox(height: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
              decoration: BoxDecoration(color: const Color(0xFFFFFBEB), borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: const Color(0xFFD97706).withOpacity(.3))),
              child: Row(children: [
                const Icon(Icons.schedule, color: Color(0xFFD97706), size: 18),
                const SizedBox(width: 8),
                Expanded(child: Text('Expected to be resolved by: ${_ticket.expectedResolution}',
                    style: const TextStyle(color: Color(0xFF92400E), fontSize: 12, fontWeight: FontWeight.w600))),
              ]),
            ),
          ],
          const SizedBox(height: 8),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12)),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const Text('Title', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: Colors.grey)),
              const SizedBox(height: 4),
              Text(_ticket.title, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15)),
              const SizedBox(height: 12),
              const Text('Description', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: Colors.grey)),
              const SizedBox(height: 4),
              Text(_ticket.description, style: const TextStyle(fontSize: 14)),
            ]),
          ),

          if (_ticket.assignedTo.isNotEmpty || _ticket.resolution.isNotEmpty) ...[
            const SizedBox(height: 12),
            _section('IT Update', [
              if (_ticket.assignedTo.isNotEmpty) _row('Assigned To', _ticket.assignedTo),
              if (_ticket.dateResolved.isNotEmpty) _row('Resolved On', _ticket.dateResolved),
              if (_ticket.resolution.isNotEmpty) _row('Resolution', _ticket.resolution),
              if (_ticket.remarks.isNotEmpty) _row('Remarks', _ticket.remarks),
            ], color: const Color(0xFF059669)),
          ],

          // Employee's own confirmation of whether the fix actually worked —
          // shown read-only to IT/admin, and as an editable form to the
          // employee once the ticket is Resolved/Closed (mirrors the web
          // "My Helpdesk" ticket details feedback card/form).
          if (_isAdmin) _feedbackDisplay(),
          if (!_isAdmin) _feedbackSection(),

          if (_isAdmin) ...[
            const SizedBox(height: 24),
            SizedBox(
              width: double.infinity,
              height: 50,
              child: ElevatedButton.icon(
                onPressed: _showUpdateDialog,
                icon: const Icon(Icons.edit),
                label: const Text('Update Ticket', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
                style: ElevatedButton.styleFrom(
                  backgroundColor: const Color(0xFF2563EB), foregroundColor: Colors.white,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                ),
              ),
            ),
          ],
          const SizedBox(height: 32),
        ],
      ),
    );
  }

  Widget _section(String title, List<Widget> rows, {Color? color}) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(12),
        border: Border(left: BorderSide(color: color ?? const Color(0xFF2563EB), width: 4)),
        boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)],
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(title, style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: color ?? const Color(0xFF2563EB))),
        const SizedBox(height: 10),
        ...rows,
      ]),
    );
  }

  // Read-only "Employee Feedback" card for IT/admin — green if the employee
  // confirmed the fix worked, red if not, matching the web Details.cshtml card.
  Widget _feedbackDisplay() {
    if (_ticket.empFeedbackStatus.isEmpty) return const SizedBox.shrink();
    final ok = _ticket.empFeedbackStatus == 'Resolved';
    final color = ok ? const Color(0xFF059669) : const Color(0xFFDC2626);
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: color.withOpacity(.06),
          borderRadius: BorderRadius.circular(12),
          border: Border(left: BorderSide(color: color, width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)],
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Icon(ok ? Icons.sentiment_satisfied_alt_rounded : Icons.sentiment_dissatisfied_rounded, color: color, size: 18),
            const SizedBox(width: 6),
            Text('Employee Feedback: ${_ticket.empFeedbackStatus}',
                style: TextStyle(fontWeight: FontWeight.bold, color: color, fontSize: 13)),
          ]),
          if (_ticket.empFeedback.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(_ticket.empFeedback, style: const TextStyle(fontSize: 13)),
          ],
          if (_ticket.empFeedbackDate.isNotEmpty) ...[
            const SizedBox(height: 6),
            Text(_ticket.empFeedbackDate, style: const TextStyle(fontSize: 10, color: Colors.grey)),
          ],
        ]),
      ),
    );
  }

  // Employee-facing feedback form — only once IT has marked the ticket
  // Resolved/Closed. Pre-filled with any previous submission so it can be
  // updated (e.g. if IT re-opens and re-resolves the ticket).
  Widget _feedbackSection() {
    final canGiveFeedback = _ticket.status == 'Resolved' || _ticket.status == 'Closed';
    if (!canGiveFeedback) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(12),
          border: const Border(left: BorderSide(color: Color(0xFF7C3AED), width: 4)),
          boxShadow: [BoxShadow(color: Colors.black.withOpacity(.04), blurRadius: 6)],
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Your Feedback', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: Color(0xFF7C3AED))),
          const SizedBox(height: 4),
          const Text('Kya problem actually resolve ho gayi?', style: TextStyle(fontSize: 11, color: Colors.grey)),
          const SizedBox(height: 6),
          Row(children: [
            Expanded(child: RadioListTile<String>(
              contentPadding: EdgeInsets.zero, dense: true,
              title: const Text('✅ Resolved', style: TextStyle(fontSize: 12)),
              value: 'Resolved', groupValue: _fbStatus,
              onChanged: (v) => setState(() => _fbStatus = v!),
            )),
            Expanded(child: RadioListTile<String>(
              contentPadding: EdgeInsets.zero, dense: true,
              title: const Text('❌ Not Resolved', style: TextStyle(fontSize: 12)),
              value: 'Not Resolved', groupValue: _fbStatus,
              onChanged: (v) => setState(() => _fbStatus = v!),
            )),
          ]),
          TextFormField(
            controller: _fbComment,
            maxLines: 3,
            decoration: const InputDecoration(
                hintText: 'Comment (optional)', border: OutlineInputBorder(), isDense: true, contentPadding: EdgeInsets.all(10)),
          ),
          const SizedBox(height: 10),
          SizedBox(
            width: double.infinity,
            height: 42,
            child: ElevatedButton(
              onPressed: _fbSubmitting ? null : _submitFeedback,
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF7C3AED), foregroundColor: Colors.white,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10))),
              child: _fbSubmitting
                  ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : Text(_ticket.empFeedbackStatus.isEmpty ? 'Submit Feedback' : 'Update Feedback',
                      style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13)),
            ),
          ),
        ]),
      ),
    );
  }

  Widget _row(String l, String v) {
    if (v.isEmpty) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(width: 100, child: Text('$l:', style: const TextStyle(color: Colors.grey, fontSize: 12, fontWeight: FontWeight.w600))),
        Expanded(child: Text(v, style: const TextStyle(fontSize: 13))),
      ]),
    );
  }
}
