import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../services/api_service.dart';
import '../app_theme.dart';
import 'home_screen.dart';
import 'admin_home_screen.dart';
import 'change_password_screen.dart';

class SetupScreen extends StatefulWidget {
  const SetupScreen({super.key});
  @override
  State<SetupScreen> createState() => _SetupScreenState();
}

class _SetupScreenState extends State<SetupScreen> {
  final _userCtrl = TextEditingController();
  final _pwdCtrl = TextEditingController();
  bool _loggingIn = false, _obscurePwd = true;
  String? _error;

  Future<void> _login() async {
    final username = _userCtrl.text.trim();
    final pwd = _pwdCtrl.text.trim();
    if (username.isEmpty || pwd.isEmpty) { setState(() => _error = 'Enter both Employee Code and Password'); return; }

    setState(() { _loggingIn = true; _error = null; });
    final result = await ApiService.login(username, pwd);
    if (result == null) {
      setState(() { _loggingIn = false; _error = 'Incorrect Employee Code or Password'; });
      return;
    }

    final isAdmin = result['isAdmin'] == 'true';
    final mustChange = result['mustChangePassword'] == 'true';
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('username', result['username']!);
    await prefs.setString('password', pwd);
    await prefs.setString('emp_id',    result['empId']!);
    await prefs.setString('emp_name',  result['name']!);
    await prefs.setString('emp_dept',  result['dept']!);
    await prefs.setString('emp_desig', result['desig']!);
    await prefs.setBool('is_admin', isAdmin);

    setState(() => _loggingIn = false);
    if (!mounted) return;
    if (mustChange) {
      Navigator.pushReplacement(context, MaterialPageRoute(builder: (_) => const ChangePasswordScreen(forced: true)));
    } else {
      Navigator.pushReplacement(context,
          MaterialPageRoute(builder: (_) => isAdmin ? const AdminHomeScreen() : const HomeScreen()));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Container(
        decoration: const BoxDecoration(gradient: AppColors.navyGradient),
        child: SafeArea(
          child: Stack(children: [
            Positioned(top: -70, right: -60, child: _glow(190, AppColors.cyan.withOpacity(.16))),
            Positioned(bottom: -90, left: -50, child: _glow(230, AppColors.blue.withOpacity(.16))),
            SingleChildScrollView(
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 36),
              child: Column(children: [
                const SizedBox(height: 12),
                const AppLogoBadge(size: 76),
                const SizedBox(height: 20),
                const Text('AMPM IT Helpdesk',
                    style: TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.bold)),
                const SizedBox(height: 6),
                Text('Log in to your account',
                    style: TextStyle(color: Colors.white.withOpacity(.65), fontSize: 13.5)),
                const SizedBox(height: 34),

                Container(
                  padding: const EdgeInsets.all(22),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(.07),
                    borderRadius: BorderRadius.circular(22),
                    border: Border.all(color: Colors.white.withOpacity(.14)),
                  ),
                  child: Column(children: [
                    _field(label: 'Employee Code', controller: _userCtrl, icon: Icons.badge_outlined),
                    const SizedBox(height: 16),
                    _field(
                      label: 'Password',
                      controller: _pwdCtrl,
                      icon: Icons.lock_outline_rounded,
                      obscure: _obscurePwd,
                      onSubmit: (_) => _login(),
                      suffix: IconButton(
                        icon: Icon(_obscurePwd ? Icons.visibility_outlined : Icons.visibility_off_outlined,
                            color: Colors.white38, size: 20),
                        onPressed: () => setState(() => _obscurePwd = !_obscurePwd),
                      ),
                    ),
                    if (_error != null) ...[
                      const SizedBox(height: 14),
                      Container(
                        width: double.infinity,
                        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                        decoration: BoxDecoration(
                          color: AppColors.red.withOpacity(.15),
                          borderRadius: BorderRadius.circular(10),
                          border: Border.all(color: AppColors.red.withOpacity(.35)),
                        ),
                        child: Row(children: [
                          const Icon(Icons.error_outline_rounded, color: Color(0xFFFCA5A5), size: 17),
                          const SizedBox(width: 8),
                          Expanded(child: Text(_error!, style: const TextStyle(color: Color(0xFFFCA5A5), fontSize: 12.5))),
                        ]),
                      ),
                    ],
                    const SizedBox(height: 22),
                    SizedBox(
                      width: double.infinity,
                      height: 52,
                      child: ElevatedButton(
                        onPressed: _loggingIn ? null : _login,
                        style: ElevatedButton.styleFrom(
                            backgroundColor: AppColors.blue,
                            foregroundColor: Colors.white,
                            disabledBackgroundColor: AppColors.blue.withOpacity(.5),
                            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14))),
                        child: _loggingIn
                            ? const SizedBox(width: 22, height: 22,
                                child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                            : const Text('Login', style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
                      ),
                    ),
                  ]),
                ),
                const SizedBox(height: 28),
                Text('AMPM Fashions Pvt. Ltd.  ·  IT Department',
                    style: TextStyle(color: Colors.white.withOpacity(.35), fontSize: 11)),
                const SizedBox(height: 12),
              ]),
            ),
          ]),
        ),
      ),
    );
  }

  Widget _glow(double size, Color color) =>
      Container(width: size, height: size, decoration: BoxDecoration(shape: BoxShape.circle, color: color));

  Widget _field({
    required String label,
    required TextEditingController controller,
    required IconData icon,
    bool obscure = false,
    Widget? suffix,
    void Function(String)? onSubmit,
  }) {
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text(label,
          style: TextStyle(color: Colors.white.withOpacity(.6), fontSize: 12, fontWeight: FontWeight.w600, letterSpacing: .3)),
      const SizedBox(height: 8),
      TextField(
        controller: controller,
        obscureText: obscure,
        onSubmitted: onSubmit,
        style: const TextStyle(color: Colors.white, fontSize: 15),
        decoration: InputDecoration(
          filled: true,
          fillColor: Colors.white.withOpacity(.08),
          hintText: label,
          hintStyle: TextStyle(color: Colors.white.withOpacity(.25)),
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
          focusedBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(12), borderSide: BorderSide(color: AppColors.cyan.withOpacity(.6))),
          prefixIcon: Icon(icon, color: AppColors.cyan, size: 20),
          suffixIcon: suffix,
          contentPadding: const EdgeInsets.symmetric(vertical: 14),
        ),
      ),
    ]);
  }
}
