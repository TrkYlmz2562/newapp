import SwiftUI

struct ContentView: View {
    @ObservedObject var settings = AppSettings.shared

    @State private var photos: [PhotoItem] = []
    @State private var isLoading = false
    @State private var errorMessage: String?
    @State private var showSettings = false
    @State private var showSlideshow = false

    var body: some View {
        NavigationStack {
            ZStack {
                // Background gradient
                LinearGradient(
                    colors: [Color(red: 0.06, green: 0.06, blue: 0.12), Color(red: 0.1, green: 0.05, blue: 0.15)],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
                .ignoresSafeArea()

                VStack(spacing: 0) {
                    if settings.instagramUsername.isEmpty {
                        welcomeView
                    } else {
                        mainContent
                    }
                }
            }
            .navigationTitle("")
            .navigationBarHidden(true)
            .fullScreenCover(isPresented: $showSlideshow) {
                SlideshowView(photos: photos)
            }
            .sheet(isPresented: $showSettings) {
                SettingsView(isPresented: $showSettings)
                    .onDisappear {
                        if !settings.instagramUsername.isEmpty && photos.isEmpty {
                            loadPhotos()
                        }
                    }
            }
        }
    }

    // MARK: - Welcome Screen
    private var welcomeView: some View {
        VStack(spacing: 32) {
            Spacer()

            // App icon area
            ZStack {
                Circle()
                    .fill(
                        LinearGradient(
                            colors: [Color(red: 0.89, green: 0.26, blue: 0.55), Color(red: 0.98, green: 0.55, blue: 0.19)],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
                    .frame(width: 120, height: 120)
                Image(systemName: "photo.stack.fill")
                    .font(.system(size: 50))
                    .foregroundColor(.white)
            }
            .shadow(color: .pink.opacity(0.4), radius: 20)

            VStack(spacing: 12) {
                Text("InstaWallpaper")
                    .font(.largeTitle.bold())
                    .foregroundColor(.white)

                Text("Instagram fotoğraflarını slayt gösterisi\nolarak izle ve duvar kağıdı olarak kaydet")
                    .font(.body)
                    .multilineTextAlignment(.center)
                    .foregroundColor(.white.opacity(0.7))
                    .padding(.horizontal, 32)
            }

            Button {
                showSettings = true
            } label: {
                HStack(spacing: 10) {
                    Image(systemName: "plus.circle.fill")
                    Text("Hesap Ekle")
                        .fontWeight(.semibold)
                }
                .foregroundColor(.white)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 16)
                .background(
                    LinearGradient(
                        colors: [Color(red: 0.89, green: 0.26, blue: 0.55), Color(red: 0.98, green: 0.55, blue: 0.19)],
                        startPoint: .leading,
                        endPoint: .trailing
                    )
                )
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .shadow(color: .pink.opacity(0.4), radius: 10, y: 4)
            }
            .padding(.horizontal, 32)

            Spacer()
        }
    }

    // MARK: - Main Content
    private var mainContent: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text("@\(settings.instagramUsername)")
                        .font(.title2.bold())
                        .foregroundColor(.white)
                    if !photos.isEmpty {
                        Text("\(photos.count) fotoğraf")
                            .font(.caption)
                            .foregroundColor(.white.opacity(0.6))
                    }
                }

                Spacer()

                Button {
                    showSettings = true
                } label: {
                    Image(systemName: "gearshape.fill")
                        .font(.title2)
                        .foregroundColor(.white.opacity(0.8))
                }
            }
            .padding(.horizontal, 20)
            .padding(.top, 60)
            .padding(.bottom, 20)

            // Content area
            if isLoading {
                loadingView
            } else if let error = errorMessage {
                errorView(error)
            } else if photos.isEmpty {
                emptyView
            } else {
                photoGrid
            }
        }
    }

    // MARK: - Loading
    private var loadingView: some View {
        VStack(spacing: 16) {
            Spacer()
            ProgressView()
                .tint(.white)
                .scaleEffect(1.5)
            Text("Fotoğraflar yükleniyor...")
                .foregroundColor(.white.opacity(0.7))
            Spacer()
        }
    }

    // MARK: - Error
    private func errorView(_ message: String) -> some View {
        VStack(spacing: 20) {
            Spacer()
            Image(systemName: "exclamationmark.triangle.fill")
                .font(.system(size: 48))
                .foregroundColor(.orange)

            Text(message)
                .multilineTextAlignment(.center)
                .foregroundColor(.white.opacity(0.8))
                .padding(.horizontal, 32)

            Button {
                loadPhotos()
            } label: {
                Label("Tekrar Dene", systemImage: "arrow.clockwise")
                    .foregroundColor(.white)
                    .padding(.horizontal, 24)
                    .padding(.vertical, 12)
                    .background(Color.white.opacity(0.15))
                    .clipShape(Capsule())
            }
            Spacer()
        }
    }

    // MARK: - Empty
    private var emptyView: some View {
        VStack(spacing: 16) {
            Spacer()
            Image(systemName: "photo.badge.exclamationmark")
                .font(.system(size: 48))
                .foregroundColor(.gray)
            Text("Fotoğraf bulunamadı")
                .foregroundColor(.white.opacity(0.6))
            Button {
                loadPhotos()
            } label: {
                Label("Yenile", systemImage: "arrow.clockwise")
                    .foregroundColor(.white)
                    .padding(.horizontal, 24)
                    .padding(.vertical, 12)
                    .background(Color.white.opacity(0.15))
                    .clipShape(Capsule())
            }
            Spacer()
        }
    }

    // MARK: - Photo Grid
    private var photoGrid: some View {
        VStack(spacing: 16) {
            // Start slideshow button
            Button {
                showSlideshow = true
            } label: {
                HStack(spacing: 12) {
                    Image(systemName: "play.fill")
                        .font(.title3)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Slayt Gösterisini Başlat")
                            .fontWeight(.semibold)
                        Text("Her \(formatInterval(settings.slideInterval)) bir fotoğraf · \(settings.transitionStyle.displayName) geçiş")
                            .font(.caption)
                            .opacity(0.8)
                    }
                    Spacer()
                    Image(systemName: "chevron.right")
                        .font(.caption)
                }
                .foregroundColor(.white)
                .padding(.horizontal, 20)
                .padding(.vertical, 16)
                .background(
                    LinearGradient(
                        colors: [Color(red: 0.89, green: 0.26, blue: 0.55), Color(red: 0.98, green: 0.55, blue: 0.19)],
                        startPoint: .leading,
                        endPoint: .trailing
                    )
                )
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .shadow(color: .pink.opacity(0.3), radius: 10, y: 4)
            }
            .padding(.horizontal, 16)

            // Refresh button row
            HStack {
                Text("Fotoğraflar")
                    .font(.headline)
                    .foregroundColor(.white)
                Spacer()
                Button {
                    loadPhotos()
                } label: {
                    Label("Yenile", systemImage: "arrow.clockwise")
                        .font(.caption)
                        .foregroundColor(.white.opacity(0.7))
                }
            }
            .padding(.horizontal, 16)

            // Grid
            ScrollView {
                LazyVGrid(
                    columns: [GridItem(.flexible(), spacing: 2), GridItem(.flexible(), spacing: 2), GridItem(.flexible(), spacing: 2)],
                    spacing: 2
                ) {
                    ForEach(Array(photos.enumerated()), id: \.element.id) { index, photo in
                        Button {
                            // Jump to this photo in slideshow
                            showSlideshow = true
                        } label: {
                            CachedAsyncImage(urlString: photo.thumbnailURL, contentMode: .fill)
                                .frame(
                                    width: (UIScreen.main.bounds.width - 4) / 3,
                                    height: (UIScreen.main.bounds.width - 4) / 3
                                )
                                .clipped()
                        }
                    }
                }
            }
        }
        .onAppear {
            // Preload first few images
            let urls = photos.prefix(6).map { $0.thumbnailURL }
            Task { await ImageCacheManager.shared.preloadImages(urls: Array(urls)) }
        }
    }

    // MARK: - Helpers
    private func loadPhotos() {
        guard !settings.instagramUsername.isEmpty else { return }
        isLoading = true
        errorMessage = nil
        photos = []

        Task {
            do {
                let fetched = try await InstagramService.shared.fetchPhotos(username: settings.instagramUsername)
                await MainActor.run {
                    photos = fetched
                    isLoading = false
                }
            } catch {
                await MainActor.run {
                    errorMessage = error.localizedDescription
                    isLoading = false
                }
            }
        }
    }

    private func formatInterval(_ seconds: Double) -> String {
        if seconds < 60 {
            return "\(Int(seconds)) sn"
        } else {
            let minutes = Int(seconds) / 60
            return "\(minutes) dk"
        }
    }
}
