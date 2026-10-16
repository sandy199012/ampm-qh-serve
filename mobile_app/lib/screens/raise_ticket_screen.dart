import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../services/api_service.dart';

class RaiseTicketScreen extends StatefulWidget {
  const RaiseTicketScreen({super.key});
  @override
  State<RaiseTicketScreen> createState() => _RaiseTicketScreenState();
}

class _RaiseTicketScreenState extends State<RaiseTicketScreen> {
  final _formKey = GlobalKey<FormState>();
  final _title   = TextEditingController();
  final _desc    = TextEditingController();
  final _phone   = TextEditingController();
  final _system  = TextEditingController();

  String? _category, _priority='Medium';
  final Set<String> _selectedIssueTypes = {};
  String _empId='',_empName='',_empDept='',_empDesig='';
  bool _submitting = false;

  // Same 36-category → issue-type classification as the website (Category
  // first, then Issue Type — kept in sync with Views/Helpdesk/Create.cshtml
  // and Views/MyHelpdesk/Index.cshtml's categoryIssueMap). Issue Type allows
  // picking more than one, joined with commas on submit — matching how
  // ASP.NET Core's StringValues.ToString() joins multiple same-named form
  // fields on the web side, so the backend needs zero changes.
  static const Map<String, List<String>> _categoryIssueMap = {
    "Hardware": ["Laptop","Desktop","Monitor","Keyboard","Mouse","Printer","Scanner","UPS","UPS Battery","HDD","SSD","RAM","Motherboard","SMPS","Adapter/Charger","Docking Station","Webcam","Speaker","Headset","Cable","Pendrive"],
    "Laptop / Desktop": ["System not starting","Slow performance","Hang","Restart/Shutdown","Blue Screen","Overheating","Battery issue","Charging issue","Display issue","Keyboard issue","Touchpad issue","Camera/Mic issue","Fan noise","Physical damage"],
    "Software": ["Software installation","Uninstallation","Software not opening","Software crash","Error message","License issue","Update/Upgrade","Configuration","Compatibility issue"],
    "Operating System": ["Windows issue","macOS issue","Boot issue","Login issue","Windows activation","OS installation","Formatting","Driver issue","Windows Update","System restore"],
    "Network / LAN": ["LAN not working","Network connectivity","Slow network","IP conflict","IP configuration","DNS issue","DHCP issue","LAN port issue","Network cable issue"],
    "Wi-Fi": ["Wi-Fi not connecting","Slow Wi-Fi","Wi-Fi authentication","Weak signal","Access Point issue","Wi-Fi password","Guest Wi-Fi"],
    "Internet": ["Internet not working","Slow internet","ISP issue","Broadband issue","Internet disconnection","Bandwidth issue","Website inaccessible"],
    "VPN / Remote Access": ["VPN not connecting","VPN credentials","VPN slow","Remote Desktop issue","Remote access permission","WFH connectivity"],
    "Email / Outlook": ["Email not sending","Email not receiving","Outlook not opening","Password issue","Mailbox full","Email configuration","Signature","Spam/Junk","Attachment issue","Sync issue","Shared mailbox"],
    "Microsoft 365": ["Outlook","Teams","OneDrive","SharePoint","Word","Excel","PowerPoint","Office activation","License assignment","MFA","Account issue"],
    "Active Directory / User Account": ["New user creation","Password reset","Account locked","Account disabled","AD group access","OU assignment","Login issue","Domain issue","Permission issue"],
    "Access / Permission": ["Folder access","Shared drive access","Application access","Server access","Email access","VPN access","Website access","Admin permission"],
    "Server": ["Server down","Server slow","Disk space","CPU/RAM issue","Service stopped","Windows Server","File Server","Application Server","Database Server","Backup issue"],
    "Backup / Recovery": ["Backup failed","Backup schedule","Data restore","File recovery","Deleted file recovery","Backup verification","Disaster recovery"],
    "Cyber Security": ["Virus","Malware","Ransomware","Phishing","Suspicious email","Suspicious link","Unauthorized software","Password/security issue","Data leakage","Security alert"],
    "Antivirus / Endpoint Security": ["Antivirus installation","License issue","Virus detection","Scan issue","Update issue","Endpoint not reporting","Policy issue"],
    "Firewall": ["Internet blocking","Application blocking","Port issue","Firewall rule","URL blocking","NAT issue","VPN rule","MAC binding"],
    "Printer / Scanner": ["Printer not working","Printing error","Paper jam","Toner issue","Network printer","Driver issue","Scanner issue","Print quality","Printer configuration"],
    "CCTV / Security Systems": ["Camera offline","Camera not recording","NVR/DVR issue","Remote viewing","Storage issue","Camera installation","Network camera issue"],
    "Biometric / Attendance": ["Device offline","Fingerprint issue","Face recognition","Attendance sync","Employee registration","Device configuration","Mobile app issue"],
    "ERP / Business Application": ["Login issue","Application error","Slow application","Access permission","Configuration","Data issue","Report issue","Integration issue"],
    "POS / Billing": ["POS login","Billing issue","Printer connection","Barcode scanner","Cash drawer","Billing software","Network issue","Sync issue"],
    "Mobile / Tablet / iPad": ["Device issue","SIM issue","Mobile data","Wi-Fi","App installation","Email configuration","MDM","Screen/charging/battery"],
    "Cloud Services": ["OneDrive","SharePoint","Google Drive","AWS","Azure","Cloud access","Storage","Sync","Permission"],
    "Software License / Compliance": ["Adobe","Corel","Microsoft Office","Antivirus","License renewal","New license","License assignment","Compliance audit"],
    "IT Asset Management": ["New asset","Asset allocation","Asset transfer","Asset return","Asset replacement","Asset audit","Asset tagging","Lost asset","Damaged asset","Scrap"],
    "New Employee IT Setup": ["Laptop/Desktop","Email ID","AD account","Software","VPN","Internet","Access permission","Printer","Mobile/SIM","IT induction"],
    "Employee Exit / IT Deactivation": ["Asset recovery","Email disable","AD disable","VPN disable","Software license removal","Data backup","Access removal"],
    "Website / Domain": ["Website down","Domain issue","DNS","SSL certificate","Hosting","Website access","Domain renewal"],
    "Telecommunication": ["Landline","Mobile SIM","Intercom","Calling issue","Call quality","SIM replacement","Recharge/plan"],
    "Audio / Video / Meeting Room": ["Projector","TV","HDMI","Video conferencing","Microphone","Speaker","Meeting room system","Camera"],
    "Power / Electrical IT Equipment": ["UPS","Power backup","Voltage issue","Adapter","Power cable","Surge protection"],
    "Data / File Management": ["File missing","File deletion","File permission","Data transfer","Data migration","Duplicate data","Storage issue"],
    "Procurement / IT Purchase": ["Laptop purchase","Desktop purchase","Software purchase","License renewal","Printer purchase","IT accessories","Vendor quotation"],
    "Vendor / AMC": ["Vendor support","AMC service","Warranty claim","Repair","Replacement","Service delay","Vendor quotation"],
    "IT Request / General": ["New requirement","IT consultation","Configuration request","Information request","IT policy/SOP","Other"],
  };

  @override
  void initState() { super.initState(); _loadProfile(); }

  Future<void> _loadProfile() async {
    final e = await ApiService.getSavedEmp();
    setState(() { _empId=e['empId']??''; _empName=e['name']??''; _empDept=e['dept']??''; _empDesig=e['desig']??''; });
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    if (_category==null) { _snack('Select a Category!'); return; }
    if (_selectedIssueTypes.isEmpty) { _snack('Select at least one Issue Type!'); return; }
    setState(() => _submitting=true);
    try {
      final t = await ApiService.raiseTicket({
        'empMobile': _phone.text.trim(), 'empHod': _system.text.trim(),
        // Comma-joined, same as the web checkbox form's multi-select —
        // the backend already expects issueType as a single joined string.
        'issueType': _selectedIssueTypes.join(','), 'category': _category??'',
        'title': _title.text.trim(), 'description': _desc.text.trim(),
        'priority': _priority, 'status': 'Open',
      });
      if (mounted) _showSuccess(t.ticketNo);
    } catch(e) { _snack('Error: $e'); }
    finally { setState(()=>_submitting=false); }
  }

  void _showSuccess(String ticketNo) {
    // Local device notification sound — confirms to the employee that their
    // ticket really went through, right as the confirmation dialog appears.
    SystemSound.play(SystemSoundType.alert);
    showDialog(context:context,barrierDismissible:false,builder:(_)=>AlertDialog(
      shape:RoundedRectangleBorder(borderRadius:BorderRadius.circular(16)),
      title:const Row(children:[Icon(Icons.check_circle,color:Color(0xFF059669),size:28),SizedBox(width:8),
          Text('Ticket Raised!',style:TextStyle(fontSize:16))]),
      content:Column(mainAxisSize:MainAxisSize.min,children:[
        Container(padding:const EdgeInsets.all(16),
          decoration:BoxDecoration(color:const Color(0xFFF0FDF4),borderRadius:BorderRadius.circular(12),
              border:Border.all(color:const Color(0xFF059669).withOpacity(.3))),
          child:Column(children:[
            const Text('Ticket Number',style:TextStyle(color:Colors.grey,fontSize:12)),
            const SizedBox(height:4),
            Text(ticketNo,style:const TextStyle(fontSize:26,fontWeight:FontWeight.bold,color:Color(0xFF6D4AFF))),
          ])),
        const SizedBox(height:12),
        const Text('The IT team has been notified!\nYou will get a response soon.',
            textAlign:TextAlign.center,style:TextStyle(fontSize:13,color:Colors.grey)),
        const SizedBox(height:8),
        Container(padding:const EdgeInsets.all(8),
          decoration:BoxDecoration(color:Colors.amber.shade50,borderRadius:BorderRadius.circular(8)),
          child:Text('Make a note of: $ticketNo',
              style:TextStyle(fontWeight:FontWeight.bold,color:Colors.amber.shade800,fontSize:12))),
      ]),
      actions:[TextButton(onPressed:(){Navigator.pop(context);Navigator.pop(context);},
          child:const Text('Done',style:TextStyle(fontWeight:FontWeight.bold,fontSize:15)))],
    ));
  }

  void _snack(String m)=>ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text(m)));

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor:const Color(0xFFF5F2FF),
      appBar:AppBar(title:const Text('Raise IT Ticket'),
          backgroundColor:const Color(0xFF130A33),foregroundColor:Colors.white),
      body:Form(key:_formKey,child:ListView(padding:const EdgeInsets.all(16),children:[
        // Employee card (read-only)
        Container(padding:const EdgeInsets.all(14),margin:const EdgeInsets.only(bottom:16),
          decoration:BoxDecoration(
              gradient:const LinearGradient(colors:[Color(0xFF130A33),Color(0xFF3A1F8A)]),
              borderRadius:BorderRadius.circular(12)),
          child:Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
            const Text('Your Details',style:TextStyle(color:Color(0xFFFF5C93),fontWeight:FontWeight.bold,fontSize:12)),
            const SizedBox(height:8),
            Text(_empName,style:const TextStyle(color:Colors.white,fontWeight:FontWeight.bold,fontSize:16)),
            const SizedBox(height:2),
            Text('$_empId  •  $_empDept',style:const TextStyle(color:Colors.white70,fontSize:12)),
          ])),
        _section('Additional Info',const Color(0xFFFF5C93),[
          _field('Phone/Extension (optional)',_phone,hint:'Mobile or desk number'),
          _field('System/Location (optional)',_system,hint:'PC name or floor'),
        ]),
        const SizedBox(height:12),
        _section('Issue Details',const Color(0xFF6D4AFF),[
          _drop('Category *',_categoryIssueMap.keys.toList(),_category,
              (v)=>setState((){_category=v;_selectedIssueTypes.clear();}),required:true),
          _issueTypeChecklist(),
          _priorityPicker(),
          _field('Title *',_title,hint:'Briefly — what is the problem?',required:true),
          _field('Description *',_desc,hint:'Describe in detail...',maxLines:4,required:true),
        ]),
        const SizedBox(height:24),
        SizedBox(width:double.infinity,height:52,
          child:ElevatedButton.icon(
            onPressed:_submitting?null:_submit,
            icon:_submitting?const SizedBox(width:20,height:20,
                child:CircularProgressIndicator(strokeWidth:2,color:Colors.white)):const Icon(Icons.send),
            label:Text(_submitting?'Submitting...':'Submit Ticket',
                style:const TextStyle(fontSize:16,fontWeight:FontWeight.bold)),
            style:ElevatedButton.styleFrom(backgroundColor:const Color(0xFF059669),foregroundColor:Colors.white,
                shape:RoundedRectangleBorder(borderRadius:BorderRadius.circular(14))),
          )),
        const SizedBox(height:32),
      ])),
    );
  }

  Widget _section(String t,Color c,List<Widget> ch)=>Container(
    margin:const EdgeInsets.only(bottom:12),
    decoration:BoxDecoration(color:Colors.white,borderRadius:BorderRadius.circular(14),
        border:Border(left:BorderSide(color:c,width:4)),
        boxShadow:[BoxShadow(color:Colors.black.withOpacity(.04),blurRadius:8)]),
    padding:const EdgeInsets.all(16),
    child:Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
      Text(t,style:TextStyle(fontWeight:FontWeight.bold,color:c,fontSize:13)),
      const SizedBox(height:14),
      ...ch.expand((w)=>[w,const SizedBox(height:12)]).toList()..removeLast(),
    ]));

  Widget _field(String lbl,TextEditingController ctrl,{String? hint,bool required=false,int maxLines=1})=>
    Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
      Text(lbl,style:const TextStyle(fontWeight:FontWeight.w600,fontSize:12,color:Color(0xFF3B3556))),
      const SizedBox(height:5),
      TextFormField(controller:ctrl,maxLines:maxLines,
        decoration:InputDecoration(hintText:hint,hintStyle:const TextStyle(color:Colors.grey,fontSize:12),
          border:OutlineInputBorder(borderRadius:BorderRadius.circular(8)),
          contentPadding:const EdgeInsets.symmetric(horizontal:12,vertical:10),isDense:true),
        validator:required?(v)=>v==null||v.isEmpty?'Required':null:null),
    ]);

  Widget _drop(String lbl,List<String> items,String? val,Function(String?) onCh,{bool required=false})=>
    Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
      Text(lbl,style:const TextStyle(fontWeight:FontWeight.w600,fontSize:12,color:Color(0xFF3B3556))),
      const SizedBox(height:5),
      DropdownButtonFormField<String>(value:val,isExpanded:true,
        items:items.map((e)=>DropdownMenuItem(value:e,child:Text(e,style:const TextStyle(fontSize:13)))).toList(),
        onChanged:onCh,
        decoration:InputDecoration(border:OutlineInputBorder(borderRadius:BorderRadius.circular(8)),
          contentPadding:const EdgeInsets.symmetric(horizontal:12,vertical:10),isDense:true),
        hint:Text('Select...',style:TextStyle(color:Colors.grey[400],fontSize:13)),
        validator:required?(v)=>v==null?'Required':null:null),
    ]);

  Widget _issueTypeChecklist(){
    final items = _category==null ? const <String>[] : (_categoryIssueMap[_category]??const []);
    return Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
      const Text('Issue Type * (select all that apply)',
          style:TextStyle(fontWeight:FontWeight.w600,fontSize:12,color:Color(0xFF3B3556))),
      const SizedBox(height:5),
      Container(
        constraints:const BoxConstraints(maxHeight:180),
        padding:const EdgeInsets.symmetric(horizontal:4),
        decoration:BoxDecoration(border:Border.all(color:const Color(0xFFD1D5DB)),
            borderRadius:BorderRadius.circular(8)),
        child: _category==null
          ? const Padding(padding:EdgeInsets.all(10),
              child:Text('Select Category first',style:TextStyle(color:Colors.grey,fontSize:12)))
          : SingleChildScrollView(
              child:Column(children:items.map((it)=>CheckboxListTile(
                dense:true,
                contentPadding:EdgeInsets.zero,
                controlAffinity:ListTileControlAffinity.leading,
                title:Text(it,style:const TextStyle(fontSize:13)),
                value:_selectedIssueTypes.contains(it),
                onChanged:(checked){
                  setState((){
                    if (checked==true) _selectedIssueTypes.add(it);
                    else _selectedIssueTypes.remove(it);
                  });
                },
              )).toList())),
      ),
    ]);
  }

  Widget _priorityPicker(){
    const ps=['Low','Medium','High','Critical'];
    const cs=[Color(0xFF059669),Color(0xFFD97706),Color(0xFFDC2626),Color(0xFF14B8A6)];
    return Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
      const Text('Priority',style:TextStyle(fontWeight:FontWeight.w600,fontSize:12,color:Color(0xFF3B3556))),
      const SizedBox(height:6),
      Row(children:List.generate(4,(i)=>Expanded(child:Padding(
        padding:EdgeInsets.only(right:i<3?8:0),
        child:GestureDetector(onTap:()=>setState(()=>_priority=ps[i]),
          child:AnimatedContainer(duration:const Duration(milliseconds:150),
            padding:const EdgeInsets.symmetric(vertical:9),
            decoration:BoxDecoration(
              color:_priority==ps[i]?cs[i]:Colors.transparent,
              border:Border.all(color:cs[i],width:1.5),
              borderRadius:BorderRadius.circular(8)),
            child:Text(ps[i],textAlign:TextAlign.center,
              style:TextStyle(color:_priority==ps[i]?Colors.white:cs[i],
                  fontWeight:FontWeight.bold,fontSize:11)))))))),
    ]);
  }
}
