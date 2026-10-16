import 'package:flutter/material.dart';

// Shared design tokens for the AMPM Helpdesk app — one place for colors,
// gradients and shadows so every screen stays visually consistent.
class AppColors {
  static const navy = Color(0xFF0A192F);
  static const navyLight = Color(0xFF16324F);
  static const cyan = Color(0xFF06B6D4);
  static const blue = Color(0xFF2563EB);
  static const green = Color(0xFF059669);
  static const amber = Color(0xFFD97706);
  static const red = Color(0xFFDC2626);
  static const purple = Color(0xFF7C3AED);
  static const bg = Color(0xFFF3F6FA);
  static const cardBorder = Color(0xFFE7ECF3);
  static const textDark = Color(0xFF0F172A);
  static const textMuted = Color(0xFF64748B);

  static const navyGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [navy, navyLight],
  );
}

class AppShadows {
  static List<BoxShadow> soft = [
    BoxShadow(color: Colors.black.withOpacity(.06), blurRadius: 18, offset: const Offset(0, 6)),
  ];
  static List<BoxShadow> tight = [
    BoxShadow(color: Colors.black.withOpacity(.05), blurRadius: 10, offset: const Offset(0, 3)),
  ];
}

// Reusable rounded "app icon" badge used on splash/login/headers.
class AppLogoBadge extends StatelessWidget {
  final double size;
  const AppLogoBadge({super.key, this.size = 72});
  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        gradient: const LinearGradient(
            colors: [AppColors.blue, AppColors.cyan], begin: Alignment.topLeft, end: Alignment.bottomRight),
        borderRadius: BorderRadius.circular(size * 0.28),
        boxShadow: [BoxShadow(color: AppColors.blue.withOpacity(.35), blurRadius: 20, offset: const Offset(0, 8))],
      ),
      child: Icon(Icons.headset_mic_rounded, color: Colors.white, size: size * 0.5),
    );
  }
}

// A polished stat tile used on both dashboards (Open / In Progress / Resolved).
class StatTile extends StatelessWidget {
  final String label;
  final int value;
  final Color color;
  final IconData icon;
  const StatTile({super.key, required this.label, required this.value, required this.color, required this.icon});

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 10),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(18),
          border: Border.all(color: AppColors.cardBorder),
          boxShadow: AppShadows.tight,
        ),
        child: Column(children: [
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(color: color.withOpacity(.12), borderRadius: BorderRadius.circular(10)),
            child: Icon(icon, color: color, size: 18),
          ),
          const SizedBox(height: 10),
          Text(value.toString(),
              style: TextStyle(color: AppColors.textDark, fontSize: 20, fontWeight: FontWeight.bold)),
          const SizedBox(height: 2),
          Text(label, style: const TextStyle(color: AppColors.textMuted, fontSize: 10.5, fontWeight: FontWeight.w600),
              textAlign: TextAlign.center),
        ]),
      ),
    );
  }
}
