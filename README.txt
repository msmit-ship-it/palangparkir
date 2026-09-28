MSM PAY STATION v1.0.0
=======================
Windows portable QRIS Pay Station untuk backend https://pay.msmparking.com

FITUR
- Native Windows WinForms, self-contained single EXE.
- Tombol Mobil / Motor pada layar.
- Zero Delay USB Encoder via Windows joystick API (auto-detect).
- Keyboard fallback: F1=Mobil, F2=Motor.
- QR display dari qr_content backend.
- Poll status pembayaran otomatis.
- Saat PAID: claim gate -> M Gate relay -> ACK server.
- Anti double-open mengikuti gate claim token backend.
- M Gate default OpenGate A0 01 01 A2, CloseGate A0 01 00 A1, pulse 800ms.
- Dry Run Relay untuk pengujian tanpa hardware.
- Log harian di folder logs.
- F12 membuka pengaturan (PIN default 1234).

SETUP PERTAMA
1. Extract ZIP ke folder, misalnya D:\MSM_PAY_STATION.
2. Jalankan MSM_PAY_STATION.exe.
3. Tekan F12, PIN default: 1234.
4. Isi Device Key dengan nilai API_DEVICE_KEY dari backend pay.msmparking.com .env.
5. Klik TEST BACKEND.
6. Selama uji tanpa relay, biarkan Dry Run aktif.
7. Untuk relay fisik, matikan Dry Run, SCAN COM, pilih COM relay, kemudian TEST OPEN GATE.
8. Zero Delay Encoder: default auto-detect joystick, Button 1=Mobil, Button 2=Motor.

UJI MOCK SAAT DOKU BELUM APPROVE
- Buat transaksi dari EXE (F1/F2 atau encoder).
- QR mock akan tampil.
- Dari admin backend, klik Simulasikan Pembayaran Berhasil.
- EXE mendeteksi PAID, claim gate, pulse relay/dry-run, lalu ACK otomatis.

CATATAN KEAMANAN
- DOKU Client Secret, Private Key, dan Admin Token TIDAK disimpan di EXE.
- EXE hanya memakai API_DEVICE_KEY.
- Jangan share config.ini production karena berisi Device Key.
- TEST OPEN GATE adalah aksi fisik. Pastikan area palang aman.

FILE SUARA OPSIONAL
Taruh file WAV berikut di folder sounds:
- paid.wav
- open.wav
Jika tidak tersedia, Windows system sound dipakai.

MSM Parking | msmparking.com
