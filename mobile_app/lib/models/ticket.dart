import 'package:flutter/material.dart';

class Ticket {
  final String ticketId;
  final String ticketNo;
  final String dateRaised;
  final String empId;
  final String empName;
  final String empEmail;
  final String empDept;
  final String empDesig;
  final String empMobile;
  final String empHod;
  final String issueType;
  final String category;
  final String title;
  final String description;
  final String priority;
  final String assignedTo;
  final String status;
  final String resolution;
  final String remarks;
  final String dateAcknowledged;
  final String dateResolved;
  final String dateClosed;
  final double resolutionHrs;
  // Expected Resolution Timeline (ETA) — set by IT on Acknowledge/In Progress,
  // shown to the employee. Raw is the datetime-local value (for re-editing),
  // the plain field is the human-readable display string.
  final String expectedResolution;
  final String expectedResolutionRaw;
  // Employee's own feedback once IT marks the ticket Resolved/Closed.
  final String empFeedbackStatus;
  final String empFeedback;
  final String empFeedbackDate;

  Ticket({
    required this.ticketId,
    required this.ticketNo,
    required this.dateRaised,
    required this.empId,
    required this.empName,
    this.empEmail = '',
    required this.empDept,
    required this.empDesig,
    required this.empMobile,
    required this.empHod,
    required this.issueType,
    required this.category,
    required this.title,
    required this.description,
    required this.priority,
    required this.assignedTo,
    required this.status,
    required this.resolution,
    required this.remarks,
    this.dateAcknowledged = '',
    required this.dateResolved,
    this.dateClosed = '',
    this.resolutionHrs = 0,
    this.expectedResolution = '',
    this.expectedResolutionRaw = '',
    this.empFeedbackStatus = '',
    this.empFeedback = '',
    this.empFeedbackDate = '',
  });

  factory Ticket.fromJson(Map<String, dynamic> j) => Ticket(
    ticketId:    j['ticketId']     ?? '',
    // Server only ever sets "ticketId" (e.g. "TKT-20260906123456") — there is
    // no separate human ticket number field in AMPMWeb. Fall back to it so
    // the mobile app always has something to show (raise-ticket confirmation,
    // ticket list, ticket detail) instead of a blank number.
    ticketNo:    j['ticketNo']     ?? j['ticketId'] ?? '',
    dateRaised:  j['dateRaised']   ?? '',
    empId:       j['empId']        ?? '',
    empName:     j['empName']      ?? '',
    empEmail:    j['empEmail']     ?? '',
    empDept:     j['empDept']      ?? '',
    empDesig:    j['empDesig']     ?? '',
    empMobile:   j['empMobile']    ?? j['empPhone'] ?? '',
    empHod:      j['empHod']       ?? '',
    issueType:   j['issueType']    ?? '',
    category:    j['category']     ?? '',
    title:       j['title']        ?? '',
    description: j['description']  ?? '',
    priority:    j['priority']     ?? 'Medium',
    assignedTo:  j['assignedTo']   ?? '',
    status:      j['status']       ?? 'Open',
    resolution:  j['resolution']   ?? '',
    remarks:     j['remarks']      ?? '',
    dateAcknowledged: j['dateAcknowledged'] ?? '',
    dateResolved:j['dateResolved'] ?? '',
    dateClosed:  j['dateClosed']   ?? '',
    resolutionHrs: (j['resolutionHrs'] is num) ? (j['resolutionHrs'] as num).toDouble() : 0,
    expectedResolution:    j['expectedResolution']    ?? '',
    expectedResolutionRaw: j['expectedResolutionRaw'] ?? '',
    empFeedbackStatus: j['empFeedbackStatus'] ?? '',
    empFeedback:       j['empFeedback']       ?? '',
    empFeedbackDate:   j['empFeedbackDate']   ?? '',
  );

  Map<String, dynamic> toJson() => {
    'ticketId': ticketId, 'ticketNo': ticketNo, 'dateRaised': dateRaised,
    'empId': empId, 'empName': empName, 'empEmail': empEmail, 'empDept': empDept,
    'empDesig': empDesig, 'empMobile': empMobile, 'empHod': empHod,
    'issueType': issueType, 'category': category, 'title': title,
    'description': description, 'priority': priority, 'assignedTo': assignedTo,
    'status': status, 'resolution': resolution, 'remarks': remarks,
    'dateAcknowledged': dateAcknowledged, 'dateResolved': dateResolved,
    'dateClosed': dateClosed, 'resolutionHrs': resolutionHrs,
    'expectedResolution': expectedResolution, 'expectedResolutionRaw': expectedResolutionRaw,
    'empFeedbackStatus': empFeedbackStatus, 'empFeedback': empFeedback, 'empFeedbackDate': empFeedbackDate,
  };

  Color get statusColor {
    switch (status) {
      case 'Open':          return const Color(0xFFDC2626);
      case 'Acknowledged':  return const Color(0xFF7C3AED);
      case 'In Progress':   return const Color(0xFFD97706);
      case 'On Hold':       return const Color(0xFF64748B);
      case 'Resolved':      return const Color(0xFF059669);
      case 'Closed':        return const Color(0xFF6B7280);
      default:              return const Color(0xFF6B7280);
    }
  }

  Color get priorityColor {
    switch (priority) {
      case 'Critical': return const Color(0xFF7C3AED);
      case 'High':     return const Color(0xFFDC2626);
      case 'Medium':   return const Color(0xFFD97706);
      case 'Low':      return const Color(0xFF059669);
      default:         return const Color(0xFF6B7280);
    }
  }
}
