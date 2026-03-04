import SwiftUI
import UIKit

struct SlideshowView: View {
    @ObservedObject var settings = AppSettings.shared
    let photos: [PhotoItem]

    @State private var currentIndex = 0
    @State private var previousIndex = 0
    @State private var isTransitioning = false
    @State private var timer: Timer?
    @State private var isPlaying = true
    @State private var showControls = true
    @State private var controlsTimer: Timer?
    @State private var showSaveAlert = false
    @State private var saveAlertMessage = ""
    @State private var isSaving = false
    @State private var shareImage: UIImage?
    @State private var showShare = false
    @State private var transitionOffset: CGFloat = 0
    @State private var transitionScale: CGFloat = 1
    @State private var transitionOpacity: Double = 1

    private var orderedPhotos: [PhotoItem] {
        settings.shuffleEnabled ? photos.shuffled() : photos
    }

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            // Photo display
            photoLayer

            // Caption overlay
            if settings.showCaption, let caption = photos[safe: currentIndex]?.caption, !caption.isEmpty {
                captionOverlay(caption)
            }

            // Controls overlay
            if showControls {
                controlsOverlay
            }

            // Progress bar
            progressBar
        }
        .statusBar(hidden: true)
        .onAppear {
            applyKeepScreenOn()
            startTimer()
            scheduleControlsHide()
        }
        .onDisappear {
            stopTimer()
            UIApplication.shared.isIdleTimerDisabled = false
        }
        .onTapGesture {
            withAnimation(.easeInOut(duration: 0.2)) {
                showControls.toggle()
            }
            if showControls {
                scheduleControlsHide()
            }
        }
        .alert("Kaydedildi", isPresented: $showSaveAlert) {
            Button("Tamam", role: .cancel) {}
            Button("Duvar Kağıdı Ayarla") {
                WallpaperHelper.shared.openWallpaperSettings()
            }
        } message: {
            Text(saveAlertMessage)
        }
        .sheet(isPresented: $showShare) {
            if let img = shareImage {
                ShareSheet(items: [img])
            }
        }
    }

    // MARK: - Photo Layer
    @ViewBuilder
    private var photoLayer: some View {
        let photo = photos[safe: currentIndex]
        let contentMode: ContentMode = settings.contentFit == .fill ? .fill : .fit

        ZStack {
            if let photo {
                CachedAsyncImage(urlString: photo.imageURL, contentMode: contentMode)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .clipped()
                    .opacity(transitionOpacity)
                    .offset(x: transitionOffset)
                    .scaleEffect(transitionScale)
                    .ignoresSafeArea()
            }
        }
    }

    // MARK: - Caption
    private func captionOverlay(_ caption: String) -> some View {
        VStack {
            Spacer()
            Text(caption)
                .lineLimit(3)
                .multilineTextAlignment(.center)
                .font(.system(size: 14, weight: .medium))
                .foregroundColor(.white)
                .padding(.horizontal, 20)
                .padding(.vertical, 12)
                .background(
                    LinearGradient(
                        colors: [.clear, .black.opacity(0.7)],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )
        }
        .ignoresSafeArea()
    }

    // MARK: - Controls Overlay
    private var controlsOverlay: some View {
        VStack {
            // Top bar
            HStack {
                Text("\(currentIndex + 1) / \(photos.count)")
                    .font(.caption)
                    .foregroundColor(.white.opacity(0.8))
                    .padding(8)
                    .background(Color.black.opacity(0.5))
                    .clipShape(Capsule())

                Spacer()

                // Save button
                Button {
                    saveCurrentPhoto()
                } label: {
                    if isSaving {
                        ProgressView()
                            .tint(.white)
                            .frame(width: 36, height: 36)
                    } else {
                        Image(systemName: "square.and.arrow.down")
                            .font(.system(size: 18, weight: .semibold))
                            .foregroundColor(.white)
                            .frame(width: 36, height: 36)
                    }
                }
                .padding(8)
                .background(Color.black.opacity(0.5))
                .clipShape(Circle())
            }
            .padding(.horizontal, 16)
            .padding(.top, 50)

            Spacer()

            // Bottom controls
            HStack(spacing: 40) {
                // Previous
                Button {
                    navigate(direction: -1)
                } label: {
                    Image(systemName: "backward.fill")
                        .font(.system(size: 24))
                        .foregroundColor(.white)
                }

                // Play/Pause
                Button {
                    togglePlayback()
                } label: {
                    Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                        .font(.system(size: 32))
                        .foregroundColor(.white)
                        .frame(width: 44, height: 44)
                }

                // Next
                Button {
                    navigate(direction: 1)
                } label: {
                    Image(systemName: "forward.fill")
                        .font(.system(size: 24))
                        .foregroundColor(.white)
                }
            }
            .padding(.horizontal, 40)
            .padding(.vertical, 20)
            .background(
                LinearGradient(
                    colors: [.black.opacity(0.7), .clear],
                    startPoint: .bottom,
                    endPoint: .top
                )
            )
        }
        .transition(.opacity)
        .ignoresSafeArea()
    }

    // MARK: - Progress Bar
    private var progressBar: some View {
        VStack {
            Spacer()
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Rectangle()
                        .fill(Color.white.opacity(0.2))
                        .frame(height: 2)
                    Rectangle()
                        .fill(Color.white.opacity(0.8))
                        .frame(
                            width: photos.isEmpty ? 0 : geo.size.width * CGFloat(currentIndex + 1) / CGFloat(photos.count),
                            height: 2
                        )
                        .animation(.linear, value: currentIndex)
                }
            }
            .frame(height: 2)
        }
        .ignoresSafeArea()
    }

    // MARK: - Timer & Navigation
    private func startTimer() {
        stopTimer()
        guard isPlaying else { return }
        timer = Timer.scheduledTimer(withTimeInterval: settings.slideInterval, repeats: true) { _ in
            advance()
        }
    }

    private func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    private func togglePlayback() {
        isPlaying.toggle()
        if isPlaying {
            startTimer()
        } else {
            stopTimer()
        }
    }

    private func advance() {
        let nextIndex = (currentIndex + 1) % photos.count
        transition(to: nextIndex)
    }

    private func navigate(direction: Int) {
        let nextIndex = (currentIndex + direction + photos.count) % photos.count
        transition(to: nextIndex)
        if isPlaying { startTimer() }
    }

    private func transition(to nextIndex: Int) {
        guard !isTransitioning, nextIndex != currentIndex else { return }
        isTransitioning = true

        switch settings.transitionStyle {
        case .fade:
            withAnimation(.easeInOut(duration: 0.5)) {
                transitionOpacity = 0
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                currentIndex = nextIndex
                withAnimation(.easeInOut(duration: 0.5)) {
                    transitionOpacity = 1
                }
                isTransitioning = false
            }

        case .slide:
            let slideOut: CGFloat = nextIndex > currentIndex ? -UIScreen.main.bounds.width : UIScreen.main.bounds.width
            withAnimation(.easeInOut(duration: 0.4)) {
                transitionOffset = slideOut
                transitionOpacity = 0
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                transitionOffset = -slideOut
                currentIndex = nextIndex
                withAnimation(.easeInOut(duration: 0.4)) {
                    transitionOffset = 0
                    transitionOpacity = 1
                }
                isTransitioning = false
            }

        case .zoom:
            withAnimation(.easeIn(duration: 0.3)) {
                transitionScale = 1.15
                transitionOpacity = 0
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                transitionScale = 0.9
                currentIndex = nextIndex
                withAnimation(.easeOut(duration: 0.3)) {
                    transitionScale = 1.0
                    transitionOpacity = 1
                }
                isTransitioning = false
            }

        case .flip:
            withAnimation(.easeInOut(duration: 0.25)) {
                transitionOpacity = 0
                transitionScale = 0.95
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                currentIndex = nextIndex
                withAnimation(.easeInOut(duration: 0.25)) {
                    transitionOpacity = 1
                    transitionScale = 1.0
                }
                isTransitioning = false
            }
        }

        // Preload next images
        let preloadIndex = (nextIndex + 1) % photos.count
        if let url = photos[safe: preloadIndex]?.imageURL {
            Task { _ = try? await ImageCacheManager.shared.image(for: url) }
        }
    }

    private func scheduleControlsHide() {
        controlsTimer?.invalidate()
        controlsTimer = Timer.scheduledTimer(withTimeInterval: 3.0, repeats: false) { _ in
            withAnimation(.easeOut(duration: 0.4)) {
                showControls = false
            }
        }
    }

    private func applyKeepScreenOn() {
        UIApplication.shared.isIdleTimerDisabled = settings.keepScreenOn
    }

    // MARK: - Save Photo
    private func saveCurrentPhoto() {
        guard let photo = photos[safe: currentIndex] else { return }
        isSaving = true
        Task {
            do {
                let image = try await ImageCacheManager.shared.image(for: photo.imageURL)
                try await WallpaperHelper.shared.saveToPhotoLibrary(image)
                await MainActor.run {
                    isSaving = false
                    saveAlertMessage = "Fotoğraf kütüphanenize kaydedildi.\nDuvar kağıdı olarak ayarlamak ister misiniz?"
                    showSaveAlert = true
                }
            } catch {
                await MainActor.run {
                    isSaving = false
                    saveAlertMessage = error.localizedDescription
                    showSaveAlert = true
                }
            }
        }
    }
}

// MARK: - Safe subscript
extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
