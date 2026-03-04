import SwiftUI

struct SettingsView: View {
    @ObservedObject var settings = AppSettings.shared
    @Binding var isPresented: Bool

    @State private var usernameInput: String = ""
    @State private var isTesting = false
    @State private var testResult: String?
    @State private var testSuccess = false

    var body: some View {
        NavigationStack {
            Form {
                // MARK: - Instagram Account
                Section {
                    HStack {
                        Image(systemName: "at")
                            .foregroundColor(.secondary)
                        TextField("instagram_kullanici_adi", text: $usernameInput)
                            .autocorrectionDisabled()
                            .textInputAutocapitalization(.never)
                            .keyboardType(.asciiCapable)
                    }

                    Button {
                        testConnection()
                    } label: {
                        HStack {
                            if isTesting {
                                ProgressView()
                                    .scaleEffect(0.8)
                                Text("Test ediliyor...")
                            } else {
                                Image(systemName: "network")
                                Text("Bağlantıyı Test Et")
                            }
                        }
                    }
                    .disabled(usernameInput.trimmingCharacters(in: .whitespaces).isEmpty || isTesting)

                    if let result = testResult {
                        HStack {
                            Image(systemName: testSuccess ? "checkmark.circle.fill" : "xmark.circle.fill")
                                .foregroundColor(testSuccess ? .green : .red)
                            Text(result)
                                .font(.caption)
                                .foregroundColor(testSuccess ? .green : .red)
                        }
                    }
                } header: {
                    Text("Instagram Hesabı")
                } footer: {
                    Text("Public (herkese açık) Instagram hesaplarını destekler.")
                }

                // MARK: - Slideshow
                Section("Slayt Gösterisi") {
                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            Text("Geçiş Süresi")
                            Spacer()
                            Text(formatInterval(settings.slideInterval))
                                .foregroundColor(.secondary)
                                .monospacedDigit()
                        }
                        Slider(
                            value: $settings.slideInterval,
                            in: 3...120,
                            step: 1
                        )
                        HStack {
                            Text("3s").font(.caption2).foregroundColor(.secondary)
                            Spacer()
                            Text("2dk").font(.caption2).foregroundColor(.secondary)
                        }
                    }

                    Picker("Geçiş Efekti", selection: $settings.transitionStyle) {
                        ForEach(TransitionStyle.allCases, id: \.self) { style in
                            Text(style.displayName).tag(style)
                        }
                    }

                    Picker("Fotoğraf Boyutu", selection: $settings.contentFit) {
                        ForEach(ContentFitMode.allCases, id: \.self) { mode in
                            Text(mode.displayName).tag(mode)
                        }
                    }

                    Toggle("Karıştır", isOn: $settings.shuffleEnabled)
                }

                // MARK: - Display
                Section("Ekran") {
                    Toggle(isOn: $settings.keepScreenOn) {
                        Label("Ekranı Açık Tut", systemImage: "sun.max")
                    }
                    Toggle(isOn: $settings.showCaption) {
                        Label("Altyazıyı Göster", systemImage: "text.bubble")
                    }
                }

                // MARK: - Wallpaper Guide
                Section {
                    VStack(alignment: .leading, spacing: 10) {
                        Label("Duvar Kağıdı Nasıl Ayarlanır?", systemImage: "info.circle")
                            .font(.headline)

                        VStack(alignment: .leading, spacing: 8) {
                            stepView(number: "1", text: "Slayt gösterisinde istediğiniz fotoğrafa gelin")
                            stepView(number: "2", text: "Kaydet butonuna (↓) basarak Fotoğraflar'a kaydedin")
                            stepView(number: "3", text: "iOS Ayarlar > Duvar Kağıdı'ndan ayarlayın")
                            Divider()
                            Text("İpucu: Tüm fotoğrafları Fotoğraflar'a kaydederek iOS Kısayollar uygulamasıyla otomatik değişim kurabilirsiniz.")
                                .font(.caption)
                                .foregroundColor(.secondary)
                        }
                    }
                    .padding(.vertical, 4)

                    Button {
                        WallpaperHelper.shared.openWallpaperSettings()
                    } label: {
                        Label("Duvar Kağıdı Ayarları'nı Aç", systemImage: "photo.on.rectangle")
                    }
                } header: {
                    Text("Duvar Kağıdı")
                }

                // MARK: - About
                Section("Hakkında") {
                    HStack {
                        Text("Versiyon")
                        Spacer()
                        Text("1.0.0").foregroundColor(.secondary)
                    }
                    Link(destination: URL(string: "https://help.instagram.com/")!) {
                        Label("Instagram Yardım", systemImage: "link")
                    }
                }
            }
            .navigationTitle("Ayarlar")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Kaydet") {
                        saveSettings()
                    }
                    .fontWeight(.semibold)
                    .disabled(usernameInput.trimmingCharacters(in: .whitespaces).isEmpty)
                }
                ToolbarItem(placement: .cancellationAction) {
                    Button("İptal") {
                        isPresented = false
                    }
                }
            }
            .onAppear {
                usernameInput = settings.instagramUsername
            }
        }
    }

    private func stepView(number: String, text: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Text(number)
                .font(.caption.bold())
                .foregroundColor(.white)
                .frame(width: 20, height: 20)
                .background(Color.accentColor)
                .clipShape(Circle())
            Text(text)
                .font(.caption)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func formatInterval(_ seconds: Double) -> String {
        if seconds < 60 {
            return "\(Int(seconds)) sn"
        } else {
            let minutes = Int(seconds) / 60
            let secs = Int(seconds) % 60
            return secs == 0 ? "\(minutes) dk" : "\(minutes)dk \(secs)sn"
        }
    }

    private func testConnection() {
        guard !usernameInput.trimmingCharacters(in: .whitespaces).isEmpty else { return }
        isTesting = true
        testResult = nil
        Task {
            do {
                let photos = try await InstagramService.shared.fetchPhotos(username: usernameInput)
                await MainActor.run {
                    isTesting = false
                    testSuccess = true
                    testResult = "\(photos.count) fotoğraf bulundu!"
                }
            } catch {
                await MainActor.run {
                    isTesting = false
                    testSuccess = false
                    testResult = error.localizedDescription
                }
            }
        }
    }

    private func saveSettings() {
        settings.instagramUsername = usernameInput.trimmingCharacters(in: .whitespaces)
            .replacingOccurrences(of: "@", with: "")
        isPresented = false
    }
}
