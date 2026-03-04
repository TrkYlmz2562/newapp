import Foundation
import Combine

class AppSettings: ObservableObject {
    static let shared = AppSettings()

    @Published var instagramUsername: String {
        didSet { UserDefaults.standard.set(instagramUsername, forKey: "instagramUsername") }
    }

    @Published var slideInterval: Double {
        didSet { UserDefaults.standard.set(slideInterval, forKey: "slideInterval") }
    }

    @Published var transitionStyle: TransitionStyle {
        didSet { UserDefaults.standard.set(transitionStyle.rawValue, forKey: "transitionStyle") }
    }

    @Published var shuffleEnabled: Bool {
        didSet { UserDefaults.standard.set(shuffleEnabled, forKey: "shuffleEnabled") }
    }

    @Published var keepScreenOn: Bool {
        didSet { UserDefaults.standard.set(keepScreenOn, forKey: "keepScreenOn") }
    }

    @Published var showCaption: Bool {
        didSet { UserDefaults.standard.set(showCaption, forKey: "showCaption") }
    }

    @Published var contentFit: ContentFitMode {
        didSet { UserDefaults.standard.set(contentFit.rawValue, forKey: "contentFit") }
    }

    private init() {
        self.instagramUsername = UserDefaults.standard.string(forKey: "instagramUsername") ?? ""
        self.slideInterval = UserDefaults.standard.double(forKey: "slideInterval").nonZeroOr(10.0)
        let transRaw = UserDefaults.standard.string(forKey: "transitionStyle") ?? TransitionStyle.fade.rawValue
        self.transitionStyle = TransitionStyle(rawValue: transRaw) ?? .fade
        self.shuffleEnabled = UserDefaults.standard.bool(forKey: "shuffleEnabled")
        self.keepScreenOn = UserDefaults.standard.object(forKey: "keepScreenOn") as? Bool ?? true
        self.showCaption = UserDefaults.standard.object(forKey: "showCaption") as? Bool ?? true
        let fitRaw = UserDefaults.standard.string(forKey: "contentFit") ?? ContentFitMode.fill.rawValue
        self.contentFit = ContentFitMode(rawValue: fitRaw) ?? .fill
    }
}

enum TransitionStyle: String, CaseIterable {
    case fade = "fade"
    case slide = "slide"
    case zoom = "zoom"
    case flip = "flip"

    var displayName: String {
        switch self {
        case .fade: return "Solma"
        case .slide: return "Kaydırma"
        case .zoom: return "Yakınlaştırma"
        case .flip: return "Çevirme"
        }
    }
}

enum ContentFitMode: String, CaseIterable {
    case fill = "fill"
    case fit = "fit"

    var displayName: String {
        switch self {
        case .fill: return "Doldur"
        case .fit: return "Sığdır"
        }
    }
}

private extension Double {
    func nonZeroOr(_ fallback: Double) -> Double {
        self == 0 ? fallback : self
    }
}
