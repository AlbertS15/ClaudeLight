// Lumi — Spotlight-style launcher: finds apps and files, and asks Claude.
// Option+Space opens the panel. Built by build.sh with swiftc, no Xcode project.

import AppKit
import Carbon.HIToolbox
import Combine
import ServiceManagement
import SwiftUI

// MARK: - Search results

struct Hit: Identifiable, Equatable {
    let id: String
    let name: String
    let path: String
    let isApp: Bool

    var url: URL { URL(fileURLWithPath: path) }
    var subtitle: String {
        let dir = (path as NSString).deletingLastPathComponent
        return dir.replacingOccurrences(of: NSHomeDirectory(), with: "~")
    }
}

/// Wraps NSMetadataQuery, the same index Spotlight reads.
final class FileSearch: NSObject {
    private var query: NSMetadataQuery?
    private var pending: DispatchWorkItem?
    private var term = ""
    var onResults: ([Hit]) -> Void = { _ in }

    func search(_ text: String) {
        pending?.cancel()
        stop()
        let term = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !term.isEmpty else {
            onResults([])
            return
        }
        let work = DispatchWorkItem { [weak self] in self?.start(term) }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.12, execute: work)
    }

    private func start(_ term: String) {
        self.term = term.lowercased()
        let escaped = term.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        let string = "kMDItemDisplayName == \"*\(escaped)*\"cd || kMDItemTextContent == \"\(escaped)*\"cdw"
        guard let predicate = NSPredicate(fromMetadataQueryString: string) else { return }

        let q = NSMetadataQuery()
        q.predicate = predicate
        q.searchScopes = [NSMetadataQueryLocalComputerScope]
        q.sortDescriptors = [NSSortDescriptor(key: "kMDItemLastUsedDate", ascending: false)]
        NotificationCenter.default.addObserver(self, selector: #selector(update), name: .NSMetadataQueryDidUpdate, object: q)
        NotificationCenter.default.addObserver(self, selector: #selector(finish), name: .NSMetadataQueryDidFinishGathering, object: q)
        query = q
        q.start()
    }

    @objc private func update() { publish() }

    @objc private func finish() {
        publish()
        query?.stop()
    }

    private func publish() {
        guard let q = query else { return }
        q.disableUpdates()
        var apps: [Hit] = []
        var files: [Hit] = []
        var seen = Set<String>()
        for i in 0..<min(q.resultCount, 400) {
            guard let item = q.result(at: i) as? NSMetadataItem,
                  let path = item.value(forAttribute: NSMetadataItemPathKey) as? String,
                  !seen.contains(path),
                  !Self.isNoise(path)
            else { continue }
            seen.insert(path)
            let name = (item.value(forAttribute: NSMetadataItemDisplayNameKey) as? String) ?? (path as NSString).lastPathComponent
            let type = item.value(forAttribute: "kMDItemContentType") as? String
            let isApp = type == "com.apple.application-bundle"
            let hit = Hit(id: path, name: isApp ? name.replacingOccurrences(of: ".app", with: "") : name, path: path, isApp: isApp)
            if isApp { apps.append(hit) } else { files.append(hit) }
        }
        q.enableUpdates()
        // Name matches first, as Spotlight does; the sort is stable, so recency holds within each group.
        let named = { (h: Hit) in h.name.lowercased().contains(self.term) }
        files = files.filter(named) + files.filter { !named($0) }
        onResults(Array(apps.prefix(5)) + Array(files.prefix(12 - min(apps.count, 5))))
    }

    /// System and app-internal files Spotlight keeps out of its own results.
    static func isNoise(_ path: String) -> Bool {
        if path.hasSuffix(".app") && (path.hasPrefix("/Applications/") || path.hasPrefix("/System/Applications/")) {
            return false
        }
        let blocked = ["/Library/", "/usr/", "/System/", "/private/", "/opt/", "/.Trash/", "/node_modules/", ".app/Contents/"]
        if blocked.contains(where: { path.contains($0) }) { return true }
        return path.split(separator: "/").contains { $0.hasPrefix(".") }
    }

    func stop() {
        if let q = query {
            q.stop()
            NotificationCenter.default.removeObserver(self, name: nil, object: q)
        }
        query = nil
    }
}

// MARK: - Settings

/// The models the picker offers; `auto` leaves the choice to Claude Code's own default.
enum ClaudeModel: String, CaseIterable, Identifiable {
    case auto = "", haiku, sonnet, opus, fable

    var id: String { rawValue }

    var title: String {
        switch self {
        case .auto: return S.modelAuto
        case .haiku: return "Haiku"
        case .sonnet: return "Sonnet"
        case .opus: return "Opus"
        case .fable: return "Fable"
        }
    }

    var note: String {
        switch self {
        case .auto: return S.noteAuto
        case .haiku: return S.noteHaiku
        case .sonnet: return S.noteSonnet
        case .opus: return S.noteOpus
        case .fable: return S.noteFable
        }
    }
}

/// A model on any OpenAI-compatible service. The API key lives in the Keychain under `id`.
struct Connection: Codable, Identifiable, Equatable {
    var id = UUID()
    var name: String
    var baseURL: String
    var model: String
}

/// Ready-made addresses for the add-connection form.
enum ServicePreset: String, CaseIterable, Identifiable {
    case openRouter, openAI, deepSeek, groq, mistral, yandex, gigaChat, ollama, lmStudio, other

    var id: String { rawValue }

    var title: String {
        switch self {
        case .openRouter: return "OpenRouter"
        case .openAI: return "OpenAI"
        case .deepSeek: return "DeepSeek"
        case .groq: return "Groq"
        case .mistral: return "Mistral"
        case .yandex: return "YandexGPT"
        case .gigaChat: return "GigaChat"
        case .ollama: return S.presetOllama
        case .lmStudio: return S.presetLmstudio
        case .other: return S.presetOther
        }
    }

    var baseURL: String {
        switch self {
        case .openRouter: return "https://openrouter.ai/api/v1"
        case .openAI: return "https://api.openai.com/v1"
        case .deepSeek: return "https://api.deepseek.com"
        case .groq: return "https://api.groq.com/openai/v1"
        case .mistral: return "https://api.mistral.ai/v1"
        case .yandex: return "https://ai.api.cloud.yandex.net/v1"
        case .gigaChat: return "https://api.giga.chat/v1"
        case .ollama: return "http://127.0.0.1:11434/v1"
        case .lmStudio: return "http://localhost:1234/v1"
        case .other: return ""
        }
    }

    var modelHint: String {
        switch self {
        case .openRouter: return S.hintExample("openai/gpt-4o")
        case .openAI: return S.hintExample("gpt-4o")
        case .deepSeek: return S.hintExample("deepseek-flash")
        case .groq: return S.hintExample("llama-3.3-70b-versatile")
        case .mistral: return S.hintExample("mistral-large-latest")
        case .yandex: return S.hintExample("gpt://b1g…/yandexgpt")
        case .gigaChat: return S.hintExample("GigaChat-2")
        case .ollama: return S.hintExample("llama3.2")
        case .lmStudio: return S.hintLoadedModel
        case .other: return S.hintModelName
        }
    }

    var needsKey: Bool { self != .ollama && self != .lmStudio }
}

/// Generic-password items in the login Keychain, one per connection.
enum Keychain {
    private static let service = "io.github.alberts15.claudelight"

    static func set(_ key: String, for id: UUID) {
        delete(id)
        guard !key.isEmpty else { return }
        let item: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString,
            kSecValueData as String: Data(key.utf8),
        ]
        SecItemAdd(item as CFDictionary, nil)
    }

    static func get(_ id: UUID) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString,
            kSecReturnData as String: true,
        ]
        var out: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func delete(_ id: UUID) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString,
        ]
        SecItemDelete(query as CFDictionary)
    }
}

/// What answers a question: a Claude model through Claude Code, or a connection.
/// Gemini models through the person's own Google login in Gemini CLI; `auto` leaves the choice to the CLI.
enum GeminiModel: String, CaseIterable, Identifiable {
    case auto = "", pro = "gemini-3.1-pro-preview", flash = "gemini-3.8-flash", lite = "gemini-3.1-flash-lite"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .auto: return "Gemini"
        case .pro: return "Gemini 3.1 Pro"
        case .flash: return "Gemini 3.8 Flash"
        case .lite: return "Gemini 3.1 Flash-Lite"
        }
    }

    var note: String {
        switch self {
        case .auto: return S.noteGeminiAuto
        case .pro: return S.noteOpus
        case .flash: return S.noteSonnet
        case .lite: return S.noteHaiku
        }
    }
}

enum Choice: Hashable {
    case claude(ClaudeModel)
    case gemini(GeminiModel)
    case codex
    case custom(UUID)

    var stored: String {
        switch self {
        case let .claude(m): return "claude:" + m.rawValue
        case let .gemini(m): return "gemini:" + m.rawValue
        case .codex: return "codex:"
        case let .custom(id): return "custom:" + id.uuidString
        }
    }

    init(stored: String) {
        if stored.hasPrefix("custom:"), let id = UUID(uuidString: String(stored.dropFirst(7))) {
            self = .custom(id)
        } else if stored.hasPrefix("codex:") {
            self = .codex
        } else if stored.hasPrefix("gemini:") {
            self = .gemini(GeminiModel(rawValue: String(stored.dropFirst(7))) ?? .auto)
        } else {
            let raw = stored.hasPrefix("claude:") ? String(stored.dropFirst(7)) : stored
            self = .claude(ClaudeModel(rawValue: raw) ?? .auto)
        }
    }
}

/// Choices kept across launches and shared by the panel, the welcome window and the menu.
final class Settings: ObservableObject {
    static let shared = Settings()

    @Published var choice: Choice {
        didSet { UserDefaults.standard.set(choice.stored, forKey: "model") }
    }
    /// Whether results offer "Ask in ChatGPT" (opens chatgpt.com on the person's own login).
    @Published var showsChatGPT: Bool {
        didSet { UserDefaults.standard.set(showsChatGPT, forKey: "showsChatGPT") }
    }
    /// Interface language code, "" for the system's. Strings read it through Lang.index.
    @Published var language: String {
        didSet { UserDefaults.standard.set(language, forKey: "language") }
    }
    @Published private(set) var connections: [Connection] {
        didSet { UserDefaults.standard.set(try? JSONEncoder().encode(connections), forKey: "connections") }
    }

    private init() {
        choice = Choice(stored: UserDefaults.standard.string(forKey: "model") ?? "")
        language = UserDefaults.standard.string(forKey: "language") ?? ""
        showsChatGPT = UserDefaults.standard.object(forKey: "showsChatGPT") as? Bool ?? true
        let data = UserDefaults.standard.data(forKey: "connections")
        connections = data.flatMap { try? JSONDecoder().decode([Connection].self, from: $0) } ?? []
        if case let .custom(id) = choice, !connections.contains(where: { $0.id == id }) { choice = .claude(.auto) }
    }

    var connection: Connection? {
        guard case let .custom(id) = choice else { return nil }
        return connections.first { $0.id == id }
    }

    func title(of c: Choice) -> String {
        switch c {
        case let .claude(m): return m.title
        case let .gemini(m): return m.title
        case .codex: return S.codexTitle
        case let .custom(id): return connections.first { $0.id == id }?.name ?? "—"
        }
    }

    func add(_ connection: Connection, key: String) {
        Keychain.set(key, for: connection.id)
        connections.append(connection)
        choice = .custom(connection.id)
    }

    func remove(_ connection: Connection) {
        Keychain.delete(connection.id)
        connections.removeAll { $0.id == connection.id }
        if choice == .custom(connection.id) { choice = .claude(.auto) }
    }
}

/// The label of the hotkey's key: Ё on a Russian layout, ` elsewhere (the same physical key).
var hotkeyKey: String { Lang.codes[Lang.index] == "ru" ? "Ё" : "`" }

/// Picks the interface language, "As in system" first.
struct LanguagePicker: View {
    @ObservedObject var settings = Settings.shared

    var body: some View {
        Picker("", selection: $settings.language) {
            Text(S.languageSystem).tag("")
            ForEach(Array(zip(Lang.codes, Lang.names)), id: \.0) { code, name in
                Text(name).tag(code)
            }
        }
        .labelsHidden()
        .fixedSize()
    }
}

extension Notification.Name {
    static let addConnection = Notification.Name("addConnection")
}

/// The model menu shown in the panel and the welcome window.
struct ModelPicker: View {
    @ObservedObject var settings = Settings.shared
    var isCompact = false

    var body: some View {
        Menu {
            Picker("Claude", selection: $settings.choice) {
                ForEach(ClaudeModel.allCases) { m in
                    Text("\(m.title) — \(m.note)").tag(Choice.claude(m))
                }
            }
            .pickerStyle(.inline)
            Picker(S.codexSection, selection: $settings.choice) {
                Text("\(S.codexTitle) — \(S.noteCodex)").tag(Choice.codex)
            }
            .pickerStyle(.inline)
            Picker(S.geminiSection, selection: $settings.choice) {
                ForEach(GeminiModel.allCases) { m in
                    Text("\(m.title) — \(m.note)").tag(Choice.gemini(m))
                }
            }
            .pickerStyle(.inline)
            if !settings.connections.isEmpty {
                Picker(S.connections, selection: $settings.choice) {
                    ForEach(settings.connections) { c in
                        Text("\(c.name) — \(c.model)").tag(Choice.custom(c.id))
                    }
                }
                .pickerStyle(.inline)
            }
            Divider()
            Button(S.connectModel) {
                NotificationCenter.default.post(name: .addConnection, object: nil)
            }
        } label: {
            Text(settings.title(of: settings.choice))
                .font(.system(size: isCompact ? 12 : 13, weight: .medium))
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .padding(.horizontal, isCompact ? 8 : 0)
        .padding(.vertical, isCompact ? 3 : 0)
        .background(
            Capsule().fill(Color.primary.opacity(isCompact ? 0.07 : 0))
        )
        .help(S.model)
    }
}

// MARK: - Other services

/// Streams a chat completion from an OpenAI-compatible endpoint, keeping the conversation for follow-ups.
final class APIRunner {
    private var task: Task<Void, Never>?
    private var history: [[String: String]] = []

    func reset() {
        cancel()
        history = []
    }

    func cancel() {
        task?.cancel()
        task = nil
    }

    func ask(_ question: String, via c: Connection, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        let base = c.baseURL.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard let url = URL(string: base + "/chat/completions") else {
            onDone(S.errBadUrl(c.name))
            return
        }
        // The question joins the history only with its answer, so a failed try never repeats in it.
        // Lumi's own Ollama models carry their own prompt; sending ours would replace it.
        let ownModel = OllamaStatus.isOllama(c.baseURL) && c.model.lowercased().hasPrefix("lumi")
        let system = ownModel ? [] : [["role": "system", "content": ClaudeRunner.systemPrompt]]
        let messages = system + history + [["role": "user", "content": question]]

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 120
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let key = Keychain.get(c.id) ?? ""
        if !key.isEmpty, !GigaChat.handles(url) {
            request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        }
        // Yandex reads the folder from "gpt://<folder>/<model>"; the OpenAI SDK sends it as the project too.
        if c.model.hasPrefix("gpt://"), let folder = c.model.dropFirst(6).split(separator: "/").first {
            request.setValue(String(folder), forHTTPHeaderField: "OpenAI-Project")
        }
        var body: [String: Any] = ["model": c.model, "messages": messages, "stream": true]
        // Thinking models in Ollama (Qwen 3.5 and the like) think first and answer 20–30 seconds later;
        // a quick bar wants the answer, so local Ollama models skip it.
        if OllamaStatus.isOllama(c.baseURL) { body["reasoning_effort"] = "none" }
        request.httpBody = try? JSONSerialization.data(withJSONObject: body)

        task = Task { [weak self] in
            var reply = ""
            do {
                var request = request
                if GigaChat.handles(url) {
                    request.setValue("Bearer \(try await GigaChat.token(for: c.id, key: key))", forHTTPHeaderField: "Authorization")
                }
                let (bytes, response) = try await URLSession.shared.bytes(for: request)
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                if status != 200 {
                    var body = ""
                    for try await line in bytes.lines { body += line }
                    throw APIError(status: status, body: body)
                }
                for try await line in bytes.lines {
                    guard line.hasPrefix("data:") else { continue }
                    let payload = line.dropFirst(5).trimmingCharacters(in: .whitespaces)
                    if payload == "[DONE]" { break }
                    guard let obj = try? JSONSerialization.jsonObject(with: Data(payload.utf8)) as? [String: Any] else { continue }
                    // Services such as OpenRouter report a failure after the 200 as an "error" event in the stream.
                    if let error = obj["error"] {
                        let detail = (error as? [String: Any])?["message"] as? String ?? "\(error)"
                        throw StreamError(message: S.errStream(c.name, detail))
                    }
                    guard let choices = obj["choices"] as? [[String: Any]],
                          let delta = choices.first?["delta"] as? [String: Any],
                          let text = delta["content"] as? String, !text.isEmpty
                    else { continue }
                    reply += text
                    await MainActor.run { onText(text) }
                }
                if reply.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    throw StreamError(message: S.errEmpty(c.name))
                }
                let full = reply
                await MainActor.run { [weak self] in
                    self?.history.append(["role": "user", "content": question])
                    self?.history.append(["role": "assistant", "content": full])
                    onDone(nil)
                }
            } catch is CancellationError {
                return
            } catch let e as StreamError {
                await MainActor.run { onDone(e.message) }
            } catch let e as APIError {
                // Ollama answers 404 for a model name it doesn't have; say which ones it does have.
                if e.status == 404, OllamaStatus.isOllama(c.baseURL), let installed = await OllamaStatus.models(),
                   !installed.isEmpty, !installed.contains(c.model) {
                    await MainActor.run { onDone(S.errOllamaModel(c.model, installed.joined(separator: ", "))) }
                    return
                }
                await MainActor.run { onDone(e.message(for: c)) }
            } catch {
                if (error as? URLError)?.code == .cancelled { return }
                if let code = (error as? URLError)?.code,
                   [.serverCertificateUntrusted, .serverCertificateHasUnknownRoot, .serverCertificateHasBadDate,
                    .serverCertificateNotYetValid, .secureConnectionFailed].contains(code) {
                    await MainActor.run { onDone(S.errCert(c.name)) }
                    return
                }
                let hint = c.baseURL.contains("localhost") ? S.errLocalHint : ""
                await MainActor.run { onDone(S.errConnect(c.name, error.localizedDescription) + hint) }
            }
        }
    }
}

/// GigaChat trades the authorization key for a 30-minute access token before each chat; tokens are reused until they expire.
enum GigaChat {
    private static let authURL = URL(string: "https://ngw.devices.sberbank.ru:9443/api/v2/oauth")!
    private static var tokens: [UUID: (token: String, expires: Date)] = [:]
    private static let lock = NSLock()

    static func handles(_ url: URL) -> Bool {
        let host = url.host ?? ""
        return host.hasSuffix("giga.chat") || host.hasSuffix("sberbank.ru")
    }

    static func token(for id: UUID, key: String) async throws -> String {
        let cached = lock.withLock { tokens[id] }
        if let cached, cached.expires.timeIntervalSinceNow > 60 { return cached.token }

        var request = URLRequest(url: authURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 30
        request.setValue("Basic \(key)", forHTTPHeaderField: "Authorization")
        request.setValue(UUID().uuidString.lowercased(), forHTTPHeaderField: "RqUID")
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.httpBody = Data("scope=GIGACHAT_API_PERS".utf8)

        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200,
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let token = json["access_token"] as? String
        else {
            // A refused key shows as a key error, like any other service.
            throw APIError(status: status == 200 ? 401 : status, body: String(data: data, encoding: .utf8) ?? "")
        }
        let expiresMs = (json["expires_at"] as? Double) ?? Date().addingTimeInterval(25 * 60).timeIntervalSince1970 * 1000
        lock.withLock { tokens[id] = (token, Date(timeIntervalSince1970: expiresMs / 1000)) }
        return token
    }
}

struct StreamError: Error {
    let message: String
}

struct APIError: Error {
    let status: Int
    let body: String

    static func isBlock(_ detail: String, _ body: String) -> Bool {
        let text = (detail + " " + body).lowercased()
        return ["security policy", "cloudflare", "blocked", "region", "country", "unsupported_country", "location is not supported"]
            .contains { text.contains($0) }
    }

    func message(for c: Connection) -> String {
        let json = try? JSONSerialization.jsonObject(with: Data(body.utf8)) as? [String: Any]
        let detail = (json?["error"] as? [String: Any])?["message"] as? String ?? (json?["error"] as? String) ?? String(body.prefix(300))
        switch status {
        // A 403 from a firewall (Cloudflare, a country block) arrives before any key check.
        case 403 where Self.isBlock(detail, body): return S.errBlocked(c.name, detail.contains("<") ? "Cloudflare" : detail)
        case 401, 403: return S.errKey(c.name, String(status), detail)
        case 404: return S.err404(c.name, detail)
        case 429: return S.err429(c.name, detail)
        default: return S.errStatus(c.name, String(status), detail)
        }
    }
}

// MARK: - Claude

/// Runs the local `claude` CLI in print mode and streams the reply text.
final class ClaudeRunner {
    private var process: Process?
    private(set) var sessionID: String?

    static let binary: String? = {
        let candidates = [
            NSHomeDirectory() + "/.local/bin/claude",
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
            NSHomeDirectory() + "/.claude/local/claude",
        ]
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }()

    static let systemPrompt = """
        You were called from a Spotlight-style quick bar on a Mac. Answer briefly and to the point, \
        in the language of the question. Use Markdown only for lists, bold and code.
        """

    func reset() {
        cancel()
        sessionID = nil
    }

    func cancel() {
        process?.terminate()
        process = nil
    }

    func ask(_ question: String, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        guard let binary = Self.binary else {
            onDone(S.errNoClaude)
            return
        }

        var args = [
            "-p", question,
            "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--append-system-prompt", Self.systemPrompt,
            "--tools", "WebSearch,WebFetch",
            "--allowedTools", "WebSearch,WebFetch",
        ]
        if case let .claude(model) = Settings.shared.choice, model != .auto { args += ["--model", model.rawValue] }
        if let id = sessionID { args += ["--resume", id] }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: binary)
        p.arguments = args
        p.currentDirectoryURL = URL(fileURLWithPath: NSHomeDirectory())
        var env = ProcessInfo.processInfo.environment
        env["PATH"] = "\(NSHomeDirectory())/.local/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        p.environment = env

        let out = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        p.standardInput = FileHandle.nullDevice

        var buffer = Data()
        var streamed = false
        var error: String?
        let handle = out.fileHandleForReading

        handle.readabilityHandler = { [weak self] h in
            let chunk = h.availableData
            guard !chunk.isEmpty else {
                h.readabilityHandler = nil
                return
            }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl)
                buffer.removeSubrange(buffer.startIndex...nl)
                guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
                let type = obj["type"] as? String

                if type == "system", let id = obj["session_id"] as? String {
                    DispatchQueue.main.async { self?.sessionID = id }
                } else if type == "stream_event",
                          let event = obj["event"] as? [String: Any],
                          let delta = event["delta"] as? [String: Any],
                          delta["type"] as? String == "text_delta",
                          let text = delta["text"] as? String {
                    streamed = true
                    DispatchQueue.main.async { onText(text) }
                } else if type == "assistant",
                          let message = obj["message"] as? [String: Any],
                          let content = message["content"] as? [[String: Any]] {
                    let text = content.compactMap { $0["text"] as? String }.joined()
                    if obj["error"] != nil {
                        error = text
                    } else if !streamed, !text.isEmpty {
                        DispatchQueue.main.async { onText(text) }
                    } else if streamed, !text.isEmpty {
                        // Separates the text of successive assistant messages (around tool use).
                        DispatchQueue.main.async { onText("\n\n") }
                    }
                } else if type == "result", obj["is_error"] as? Bool == true, error == nil {
                    error = (obj["result"] as? String) ?? S.errClaude
                }
            }
        }

        p.terminationHandler = { [weak self] proc in
            DispatchQueue.main.async {
                if self?.process === proc { self?.process = nil }
                if proc.terminationReason == .uncaughtSignal { return }
                if let e = error, e.localizedCaseInsensitiveContains("authenticate") || e.localizedCaseInsensitiveContains("login") {
                    onDone(S.errNotLoggedIn)
                } else {
                    onDone(error)
                }
            }
        }

        do {
            try p.run()
            process = p
        } catch {
            onDone(S.errStart(error.localizedDescription))
        }
    }
}

// MARK: - Gemini

/// Runs Gemini CLI (`gemini`) headless on the person's own Google login and streams the reply.
/// Gemini CLI keeps no conversation between headless runs here, so follow-ups carry the history in the prompt.
final class GeminiRunner {
    private var process: Process?
    private var history: [(question: String, answer: String)] = []

    static var binary: String? {
        let home = NSHomeDirectory()
        var candidates = ["/opt/homebrew/bin/gemini", "/usr/local/bin/gemini", home + "/.local/bin/gemini", home + "/.npm-global/bin/gemini"]
        // nvm keeps one bin folder per Node version.
        if let versions = try? FileManager.default.contentsOfDirectory(atPath: home + "/.nvm/versions/node") {
            candidates += versions.sorted().reversed().map { home + "/.nvm/versions/node/\($0)/bin/gemini" }
        }
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    /// An empty folder to run in, so Gemini never reads or indexes the home folder.
    private static var workFolder: URL {
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Lumi/gemini", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    func reset() {
        cancel()
        history = []
    }

    func cancel() {
        process?.terminate()
        process = nil
    }

    func ask(_ question: String, model: GeminiModel, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        guard let binary = Self.binary else {
            onDone(S.errNoGemini)
            return
        }

        // The conversation goes in on stdin; -p adds the instructions after it.
        var input = ""
        for turn in history {
            input += "User: \(turn.question)\n\nAssistant: \(turn.answer)\n\n"
        }
        input += history.isEmpty ? question : "User: \(question)"

        var args = ["-p", ClaudeRunner.systemPrompt, "--output-format", "stream-json", "--approval-mode", "plan"]
        if model != .auto { args += ["--model", model.rawValue] }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: binary)
        p.arguments = args
        p.currentDirectoryURL = Self.workFolder
        var env = ProcessInfo.processInfo.environment
        let nodeDir = (binary as NSString).deletingLastPathComponent
        env["PATH"] = "\(nodeDir):/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        p.environment = env

        let out = Pipe()
        let inPipe = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        p.standardInput = inPipe

        var buffer = Data()
        var reply = ""
        var error: String?
        out.fileHandleForReading.readabilityHandler = { h in
            let chunk = h.availableData
            guard !chunk.isEmpty else {
                h.readabilityHandler = nil
                return
            }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl)
                buffer.removeSubrange(buffer.startIndex...nl)
                guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
                switch obj["type"] as? String {
                case "message" where obj["role"] as? String == "assistant":
                    if let text = obj["content"] as? String, !text.isEmpty {
                        reply += text
                        DispatchQueue.main.async { onText(text) }
                    }
                case "error" where obj["severity"] as? String == "error":
                    error = obj["message"] as? String
                case "result" where obj["status"] as? String == "error":
                    error = ((obj["error"] as? [String: Any])?["message"] as? String) ?? error ?? S.errGemini("")
                default:
                    break
                }
            }
        }

        p.terminationHandler = { [weak self] proc in
            DispatchQueue.main.async {
                guard self?.process === proc else { return } // cancelled or replaced
                self?.process = nil
                let text = error ?? ""
                if ["auth", "login", "credential", "sign in"].contains(where: { text.localizedCaseInsensitiveContains($0) }) {
                    onDone(S.errGeminiLogin)
                } else if let e = error {
                    onDone(S.errGemini(e))
                } else if proc.terminationStatus != 0 && reply.isEmpty {
                    onDone(S.errGemini("exit \(proc.terminationStatus)"))
                } else {
                    self?.history.append((question, reply))
                    onDone(nil)
                }
            }
        }

        do {
            try p.run()
            process = p
            inPipe.fileHandleForWriting.write(Data(input.utf8))
            try? inPipe.fileHandleForWriting.close()
        } catch {
            onDone(S.errGemini(error.localizedDescription))
        }
    }
}

// MARK: - Codex

/// Runs Codex CLI (`codex exec`) read-only on the person's own ChatGPT login and returns its answer.
/// Codex sends the answer whole, not in pieces; follow-ups carry the conversation in the prompt.
final class CodexRunner {
    private var process: Process?
    private var history: [(question: String, answer: String)] = []

    static var binary: String? {
        let home = NSHomeDirectory()
        var candidates = ["/opt/homebrew/bin/codex", "/usr/local/bin/codex", home + "/.local/bin/codex", home + "/.npm-global/bin/codex"]
        if let versions = try? FileManager.default.contentsOfDirectory(atPath: home + "/.nvm/versions/node") {
            candidates += versions.sorted().reversed().map { home + "/.nvm/versions/node/\($0)/bin/codex" }
        }
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    /// An empty folder to run in, so Codex never reads the home folder.
    private static var workFolder: URL {
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Lumi/codex", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    func reset() {
        cancel()
        history = []
    }

    func cancel() {
        process?.terminate()
        process = nil
    }

    func ask(_ question: String, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        guard let binary = Self.binary else {
            onDone(S.errNoCodex)
            return
        }

        var input = ClaudeRunner.systemPrompt + " Do not run commands or read files; just answer.\n\n"
        for turn in history {
            input += "User: \(turn.question)\n\nAssistant: \(turn.answer)\n\n"
        }
        input += history.isEmpty ? question : "User: \(question)"

        let p = Process()
        p.executableURL = URL(fileURLWithPath: binary)
        p.arguments = ["exec", "--json", "--skip-git-repo-check", "--ephemeral",
                       "--sandbox", "read-only", "--cd", Self.workFolder.path, "-"]
        p.currentDirectoryURL = Self.workFolder
        var env = ProcessInfo.processInfo.environment
        env["PATH"] = "\((binary as NSString).deletingLastPathComponent):/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        p.environment = env

        let out = Pipe()
        let inPipe = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        p.standardInput = inPipe

        var buffer = Data()
        var reply = ""
        var error: String?
        out.fileHandleForReading.readabilityHandler = { h in
            let chunk = h.availableData
            guard !chunk.isEmpty else {
                h.readabilityHandler = nil
                return
            }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl)
                buffer.removeSubrange(buffer.startIndex...nl)
                guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
                switch obj["type"] as? String {
                case "item.completed":
                    let item = obj["item"] as? [String: Any]
                    if item?["type"] as? String == "agent_message", let text = item?["text"] as? String, !text.isEmpty {
                        let piece = reply.isEmpty ? text : "\n\n" + text
                        reply += piece
                        DispatchQueue.main.async { onText(piece) }
                    }
                case "turn.failed":
                    error = ((obj["error"] as? [String: Any])?["message"] as? String) ?? error
                case "error":
                    error = (obj["message"] as? String) ?? error
                default:
                    break
                }
            }
        }

        p.terminationHandler = { [weak self] proc in
            DispatchQueue.main.async {
                guard self?.process === proc else { return } // cancelled or replaced
                self?.process = nil
                let text = error ?? ""
                if ["login", "logged in", "auth", "401", "unauthorized"].contains(where: { text.localizedCaseInsensitiveContains($0) }) {
                    onDone(S.errCodexLogin)
                } else if let e = error, reply.isEmpty {
                    onDone(S.errCodex(e))
                } else if proc.terminationStatus != 0 && reply.isEmpty {
                    onDone(S.errCodex("exit \(proc.terminationStatus)"))
                } else {
                    self?.history.append((question, reply))
                    onDone(nil)
                }
            }
        }

        do {
            try p.run()
            process = p
            inPipe.fileHandleForWriting.write(Data(input.utf8))
            try? inPipe.fileHandleForWriting.close()
        } catch {
            onDone(S.errCodex(error.localizedDescription))
        }
    }
}

// MARK: - Model

enum Row: Equatable {
    case ask
    case chatGPT
    case hit(Hit)
}

/// Opens chatgpt.com with the question filled in: it runs in the browser on the person's own ChatGPT login.
func openInChatGPT(_ question: String) {
    var allowed = CharacterSet.urlQueryAllowed
    allowed.remove(charactersIn: "&+=?#")
    let q = question.addingPercentEncoding(withAllowedCharacters: allowed) ?? ""
    if let url = URL(string: "https://chatgpt.com/?q=" + q) { NSWorkspace.shared.open(url) }
}

final class LauncherModel: ObservableObject {
    @Published var query = "" {
        didSet {
            guard query != oldValue else { return }
            if answer != nil, !isAnswering {
                // Typing after an answer starts a follow-up, keep it visible until Enter.
                isFollowUp = true
            }
            if answer == nil {
                search.search(query)
                selection = looksLikeQuestion(query) || hits.isEmpty ? 0 : firstHit
            }
        }
    }
    @Published var hits: [Hit] = []
    @Published var selection = 0
    @Published var answer: String?
    @Published var asked = ""
    @Published var isAnswering = false
    @Published var answerError: String?
    @Published var isFollowUp = false

    let search = FileSearch()
    let claude = ClaudeRunner()
    let api = APIRunner()
    let gemini = GeminiRunner()
    let codex = CodexRunner()
    var hide: () -> Void = {}

    init() {
        search.onResults = { [weak self] hits in
            guard let self else { return }
            self.hits = hits
            let maxIndex = hits.count
            if self.selection > maxIndex { self.selection = maxIndex }
            // A search lands on the first file or app; a question stays on the ask row.
            if !self.looksLikeQuestion(self.query), self.selection < self.firstHit, !hits.isEmpty { self.selection = self.firstHit }
        }
        // Published fires before the value is stored, so retry on the next turn of the run loop.
        choiceWatch = Settings.shared.$choice.dropFirst().sink { [weak self] _ in
            DispatchQueue.main.async { self?.retryAfterError() }
        }
    }

    private var choiceWatch: AnyCancellable?

    /// Asks the last question again when its answer failed: Enter on an empty bar, or a switch of model.
    func retryAfterError() {
        guard answer != nil, answerError != nil, !isAnswering, !asked.isEmpty else { return }
        claude.reset()
        api.reset()
        gemini.reset()
        codex.reset()
        ask(asked)
    }

    var rows: [Row] {
        guard !query.trimmingCharacters(in: .whitespaces).isEmpty else { return [] }
        return [.ask] + (Settings.shared.showsChatGPT ? [.chatGPT] : []) + hits.map { .hit($0) }
    }

    /// Where the file results start, after the ask rows.
    var firstHit: Int { Settings.shared.showsChatGPT ? 2 : 1 }

    /// ⇧⌘↩ or the ChatGPT row: the typed text, else the question on screen.
    func askChatGPT() {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        let question = text.isEmpty ? asked : text
        guard !question.isEmpty else { return }
        openInChatGPT(question)
        hide()
    }

    func looksLikeQuestion(_ text: String) -> Bool {
        let t = text.trimmingCharacters(in: .whitespaces).lowercased()
        if t.hasSuffix("?") { return true }
        if t.split(separator: " ").count >= 4 { return true }
        let first = String(t.split(separator: " ").first ?? "")
        return Lang.questionWords.contains(first)
    }

    func move(_ delta: Int) {
        let count = rows.count
        guard count > 0 else { return }
        selection = (selection + delta + count) % count
    }

    func activate(forceAsk: Bool = false, reveal: Bool = false) {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else {
            retryAfterError()
            return
        }
        if answer != nil || forceAsk || selection == 0 || rows.count <= selection {
            ask(text)
            return
        }
        if rows[selection] == .chatGPT {
            askChatGPT()
            return
        }
        if case let .hit(hit) = rows[selection] {
            if reveal {
                NSWorkspace.shared.activateFileViewerSelecting([hit.url])
            } else {
                NSWorkspace.shared.open(hit.url)
            }
            hide()
        }
    }

    func ask(_ text: String) {
        search.stop()
        asked = text
        answer = ""
        answerError = nil
        isAnswering = true
        isFollowUp = false
        query = ""
        let onText: (String) -> Void = { [weak self] chunk in
            self?.answer = (self?.answer ?? "") + chunk
        }
        let onDone: (String?) -> Void = { [weak self] error in
            self?.isAnswering = false
            self?.answerError = error
        }
        claude.cancel()
        api.cancel()
        gemini.cancel()
        codex.cancel()
        if let connection = Settings.shared.connection {
            api.ask(text, via: connection, onText: onText, onDone: onDone)
        } else if case let .gemini(model) = Settings.shared.choice {
            gemini.ask(text, model: model, onText: onText, onDone: onDone)
        } else if Settings.shared.choice == .codex {
            codex.ask(text, onText: onText, onDone: onDone)
        } else {
            claude.ask(text, onText: onText, onDone: onDone)
        }
    }

    /// Esc: stop an answer, leave the answer view, or hide the panel.
    func escape() {
        if isAnswering {
            claude.cancel()
            api.cancel()
            gemini.cancel()
            codex.cancel()
            isAnswering = false
        } else if answer != nil {
            answer = nil
            answerError = nil
            claude.reset()
            api.reset()
            gemini.reset()
            codex.reset()
            query = ""
        } else if !query.isEmpty {
            query = ""
            hits = []
        } else {
            hide()
        }
    }

    func copyAnswer() {
        guard let a = answer, !a.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(a, forType: .string)
    }

    func resetIfStale() {
        answer = nil
        answerError = nil
        isAnswering = false
        claude.reset()
        api.reset()
        gemini.reset()
        codex.reset()
        query = ""
        hits = []
        selection = 0
    }
}

// MARK: - Views

struct LauncherView: View {
    @ObservedObject var model: LauncherModel
    @ObservedObject var settings = Settings.shared
    @FocusState private var isFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                MascotView(isWalking: model.isAnswering)
                    .frame(width: 26, height: 24)
                TextField(model.answer != nil ? S.followupPlaceholder : S.searchPlaceholder, text: $model.query)
                    .textFieldStyle(.plain)
                    .font(.system(size: 24, weight: .regular))
                    .focused($isFocused)
                    .onSubmit { model.activate() }
                if model.isAnswering {
                    ProgressView().controlSize(.small)
                }
                ModelPicker(isCompact: true)
            }
            .padding(.horizontal, 18)
            .frame(height: 58)

            if model.answer != nil {
                Divider()
                AnswerView(model: model)
            } else if !model.rows.isEmpty {
                Divider()
                ResultsView(model: model)
            }
        }
        .frame(width: 720)
        .id(settings.language)
        .background(VisualEffect().clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous)))
        .overlay(RoundedRectangle(cornerRadius: 26, style: .continuous).strokeBorder(Color.primary.opacity(0.12)))
        .onAppear { isFocused = true }
        .onReceive(NotificationCenter.default.publisher(for: .launcherShown)) { _ in isFocused = true }
    }
}

struct ResultsView: View {
    @ObservedObject var model: LauncherModel

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(spacing: 2) {
                    ForEach(Array(model.rows.enumerated()), id: \.offset) { index, row in
                        RowView(row: row, query: model.query, isSelected: index == model.selection)
                            .id(index)
                            .contentShape(Rectangle())
                            .onTapGesture {
                                model.selection = index
                                model.activate()
                            }
                    }
                }
                .padding(8)
            }
            .frame(maxHeight: 420)
            .fixedSize(horizontal: false, vertical: true)
            .onChange(of: model.selection) { _, s in proxy.scrollTo(s) }
        }
    }
}

struct RowView: View {
    let row: Row
    let query: String
    let isSelected: Bool

    var body: some View {
        HStack(spacing: 10) {
            switch row {
            case .ask:
                MascotView(color: isSelected ? .white : .ghost, eyes: isSelected ? nil : .black)
                    .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 1) {
                    Text(S.askRow(Settings.shared.title(of: Settings.shared.choice))).font(.system(size: 14, weight: .semibold))
                    Text(query).font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer()
                Text("⌘↩").font(.system(size: 12)).foregroundStyle(.tertiary)
            case .chatGPT:
                Image(systemName: "bubble.left.and.text.bubble.right.fill")
                    .font(.system(size: 17))
                    .foregroundStyle(isSelected ? Color.white : Color(red: 0.06, green: 0.64, blue: 0.5))
                    .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 1) {
                    Text(S.askChatgpt).font(.system(size: 14, weight: .semibold))
                    Text(S.chatgptNote).font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer()
                Text("⇧⌘↩").font(.system(size: 12)).foregroundStyle(.tertiary)
            case let .hit(hit):
                Image(nsImage: NSWorkspace.shared.icon(forFile: hit.path))
                    .resizable()
                    .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 1) {
                    Text(hit.name).font(.system(size: 14, weight: .medium)).lineLimit(1)
                    Text(hit.isApp ? S.application : hit.subtitle)
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
                Spacer()
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(isSelected ? Color.accentColor.opacity(0.85) : Color.clear)
        )
        .foregroundStyle(isSelected ? Color.white : Color.primary)
    }
}

struct AnswerView: View {
    @ObservedObject var model: LauncherModel

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 10) {
                    Text(model.asked)
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(.secondary)
                    if let answer = model.answer, !answer.isEmpty {
                        Text(markdown(answer))
                            .font(.system(size: 15))
                            .textSelection(.enabled)
                            .lineSpacing(3)
                    } else if model.isAnswering {
                        Text(S.thinking).foregroundStyle(.secondary)
                    }
                    if let error = model.answerError {
                        Text(markdown(error)).foregroundStyle(.red)
                    }
                    Color.clear.frame(height: 1).id("end")
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(18)
            }
            .frame(maxHeight: 460)
            .fixedSize(horizontal: false, vertical: true)
            .onChange(of: model.answer) { _, _ in proxy.scrollTo("end", anchor: .bottom) }
        }
        HStack {
            Text(model.isAnswering ? S.hintStop : S.hintDone("⌘C"))
            Spacer()
        }
        .font(.system(size: 11))
        .foregroundStyle(.tertiary)
        .padding(.horizontal, 18)
        .padding(.bottom, 10)
    }

    private func markdown(_ s: String) -> AttributedString {
        let options = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        return (try? AttributedString(markdown: s, options: options)) ?? AttributedString(s)
    }
}

extension Color {
    /// The ghost's own teal, all year round.
    static let ghost = Color(red: 93 / 255, green: 202 / 255, blue: 165 / 255)
    /// The accent for buttons and links: teal, or pumpkin orange in the Halloween week.
    static var lumi: Color { Mascot.isHalloween ? Color(red: 1, green: 138 / 255, blue: 40 / 255) : ghost }
}

/// Lumi, the glowing ghost: B body, E eyes and mouth.
/// `step` 1 is the floating pose: the ghost rises a pixel and its hem ripples the other way.
enum Mascot {
    static let width = 14
    static var height: Int { isHalloween ? 12 + hat.count : 12 }

    /// From October 24 to November 1 Lumi wears a witch's hat and the accent turns pumpkin orange.
    /// LUMI_HALLOWEEN=1 forces it on (and =0 off), to try it any day.
    static let isHalloween: Bool = {
        if let forced = ProcessInfo.processInfo.environment["LUMI_HALLOWEEN"] { return forced == "1" }
        let d = Calendar.current.dateComponents([.month, .day], from: Date())
        return (d.month == 10 && d.day! >= 24) || (d.month == 11 && d.day! == 1)
    }()

    /// The hat: H crown and brim, O the orange band.
    private static let hat = [
        "........HH....",
        ".......HHH....",
        "......HHHH....",
        ".....OOOOOO...",
        "..HHHHHHHHHH..",
    ]

    private static let rows = [
        "....BBBBBB....",
        "..BBBBBBBBBB..",
        ".BBBBBBBBBBBB.",
        ".BBBBBBBBBBBB.",
        "BBBEEBBBBEEBBB",
        "BBBEEBBBBEEBBB",
        "BBBBBBBBBBBBBB",
        "BBBBBBEEBBBBBB",
        "BBBBBBBBBBBBBB",
        "BBBBBBBBBBBBBB",
    ]
    private static let hems = ["BB..BBBBBB..BB", "..BBBB..BBBB.."]

    /// Each lit pixel as (column, row, isEye), row 0 at the top. With the hat on, the ghost sits below it.
    static func pixels(step: Int = 0, withHat: Bool = true) -> [(x: Int, y: Int, isEye: Bool)] {
        let lift = step == 1 ? 0 : 1
        let top = withHat && isHalloween ? hat.count : 0
        var out: [(x: Int, y: Int, isEye: Bool)] = []
        for (r, line) in (rows + [hems[step]]).enumerated() {
            for (c, ch) in line.enumerated() where ch != "." {
                out.append((c, r + lift + top, ch == "E"))
            }
        }
        return out
    }

    /// The hat's pixels as (column, row, isBand); empty outside the Halloween week. It bobs with the ghost.
    static func hatPixels(step: Int = 0) -> [(x: Int, y: Int, isBand: Bool)] {
        guard isHalloween else { return [] }
        let lift = step == 1 ? 0 : 1
        var out: [(x: Int, y: Int, isBand: Bool)] = []
        for (r, line) in hat.enumerated() {
            for (c, ch) in line.enumerated() where ch != "." {
                out.append((c, r + lift, ch == "O"))
            }
        }
        return out
    }
}

/// Lumi in the panel; it walks while `isWalking`. `eyes` nil cuts the eyes out instead.
struct MascotView: View {
    var color: Color = .ghost
    var eyes: Color? = .black
    var isWalking = false

    var body: some View {
        TimelineView(.animation(minimumInterval: 0.18, paused: !isWalking)) { context in
            let step = isWalking ? Int(context.date.timeIntervalSinceReferenceDate / 0.18) % 2 : 0
            Canvas { ctx, size in
                let u = min(size.width / CGFloat(Mascot.width), size.height / CGFloat(Mascot.height))
                let x0 = (size.width - u * CGFloat(Mascot.width)) / 2
                let y0 = (size.height - u * CGFloat(Mascot.height)) / 2
                for p in Mascot.pixels(step: step) {
                    // A hair of overlap so neighbouring pixels never show a seam.
                    let rect = CGRect(x: x0 + CGFloat(p.x) * u, y: y0 + CGFloat(p.y) * u, width: u + 0.4, height: u + 0.4)
                    var layer = ctx
                    if p.isEye, eyes == nil { layer.blendMode = .destinationOut }
                    layer.fill(Path(rect), with: .color(p.isEye ? (eyes ?? color) : color))
                }
                for p in Mascot.hatPixels(step: step) {
                    let rect = CGRect(x: x0 + CGFloat(p.x) * u, y: y0 + CGFloat(p.y) * u, width: u + 0.4, height: u + 0.4)
                    let hat = Color(red: 0.36, green: 0.2, blue: 0.52), band = Color(red: 1, green: 138 / 255, blue: 40 / 255)
                    ctx.fill(Path(rect), with: .color(p.isBand ? band : hat))
                }
            }
            .compositingGroup()
        }
    }
}

struct VisualEffect: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = .popover
        v.blendingMode = .behindWindow
        v.state = .active
        return v
    }

    func updateNSView(_ v: NSVisualEffectView, context: Context) {}
}

extension Notification.Name {
    static let launcherShown = Notification.Name("launcherShown")
}

// MARK: - Panel

final class LauncherPanel: NSPanel {
    var model: LauncherModel?

    override var canBecomeKey: Bool { true }

    override func resignKey() {
        super.resignKey()
        orderOut(nil)
    }

    override func keyDown(with event: NSEvent) {
        if handle(event) { return }
        super.keyDown(with: event)
    }

    /// Keys the panel answers before the text field sees them.
    func handle(_ event: NSEvent) -> Bool {
        guard let model, isKeyWindow else { return false }
        let cmd = event.modifierFlags.contains(.command)
        switch Int(event.keyCode) {
        case kVK_Escape:
            model.escape()
            return true
        case kVK_DownArrow where model.answer == nil:
            model.move(1)
            return true
        case kVK_UpArrow where model.answer == nil:
            model.move(-1)
            return true
        case kVK_Return, kVK_ANSI_KeypadEnter:
            if cmd && event.modifierFlags.contains(.shift) {
                model.askChatGPT()
                return true
            }
            if cmd {
                model.activate(forceAsk: true)
                return true
            }
            if event.modifierFlags.contains(.option) {
                model.activate(reveal: true)
                return true
            }
            return false
        case kVK_ANSI_C where cmd && model.answer != nil && model.query.isEmpty:
            if let editor = firstResponder as? NSTextView, editor.selectedRange().length > 0 { return false }
            model.copyAnswer()
            return true
        default:
            return false
        }
    }
}

// MARK: - Welcome window

/// What the welcome window shows and changes: Claude's sign-in, the login item, the window itself.
final class WelcomeModel: ObservableObject {
    enum Auth { case checking, signedIn, signedOut, missing }

    @Published var auth: Auth = .checking
    @Published var isMascotWalking = false
    @Published var isAddingConnection = false
    let form = ConnectionForm()

    func startAdding() {
        form.reset()
        isAddingConnection = true
    }

    /// The offline route: the add-connection form, already on Ollama.
    func startAddingOllama() {
        form.reset()
        form.preset = .ollama
        isAddingConnection = true
    }

    func saveConnection() {
        let c = Connection(
            name: form.name.trimmingCharacters(in: .whitespaces),
            baseURL: form.baseURL.trimmingCharacters(in: .whitespaces),
            model: form.model.trimmingCharacters(in: .whitespaces)
        )
        Settings.shared.add(c, key: form.key.trimmingCharacters(in: .whitespacesAndNewlines))
        isAddingConnection = false
    }
    @Published var launchesAtLogin = SMAppService.mainApp.status == .enabled
    @Published var showsOnLaunch: Bool {
        didSet { UserDefaults.standard.set(showsOnLaunch, forKey: "showsWelcomeOnLaunch") }
    }

    var openSearch: () -> Void = {}
    var onLoginItemChange: () -> Void = {}

    init() {
        showsOnLaunch = UserDefaults.standard.object(forKey: "showsWelcomeOnLaunch") as? Bool ?? true
    }

    /// Asks `claude auth status`, which answers from the local credentials without a model call.
    func checkAuth() {
        guard let binary = ClaudeRunner.binary else {
            auth = .missing
            return
        }
        auth = .checking
        DispatchQueue.global().async {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: binary)
            p.arguments = ["auth", "status"]
            let out = Pipe()
            p.standardOutput = out
            p.standardError = Pipe()
            var isSignedIn = false
            if (try? p.run()) != nil {
                let data = out.fileHandleForReading.readDataToEndOfFile()
                p.waitUntilExit()
                let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
                isSignedIn = json?["loggedIn"] as? Bool ?? false
            }
            DispatchQueue.main.async { self.auth = isSignedIn ? .signedIn : .signedOut }
        }
    }

    /// Opens Terminal on `claude auth login` through a throwaway .command file (no Automation permission needed).
    func signIn() {
        guard let binary = ClaudeRunner.binary else { return }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("claudelight-login.command")
        let script = "#!/bin/zsh\n'\(binary)' auth login\necho\necho '\(S.loginDone.replacingOccurrences(of: "'", with: ""))'\n"
        try? script.write(to: url, atomically: true, encoding: .utf8)
        try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.path)
        NSWorkspace.shared.open(url)
    }

    func setLaunchesAtLogin(_ isOn: Bool) {
        do {
            if isOn { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            NSSound.beep()
        }
        launchesAtLogin = SMAppService.mainApp.status == .enabled
        onLoginItemChange()
    }
}

/// The add-connection form's fields.
/// Whether Ollama answers on this computer, and which models it has.
enum OllamaStatus: Equatable {
    case checking, missing, ready([String])

    static let downloadPage = URL(string: "https://ollama.com/download")!

    /// Whether an address points at Ollama on this computer.
    static func isOllama(_ address: String) -> Bool {
        address.contains("localhost:11434") || address.contains("127.0.0.1:11434")
    }

    /// Ollama's installed models, or nil when it doesn't answer. 127.0.0.1 rather than localhost: Ollama listens on
    /// IPv4 only, and trying IPv6 first can eat the timeout.
    static func models() async -> [String]? {
        var request = URLRequest(url: URL(string: "http://127.0.0.1:11434/api/tags")!)
        request.timeoutInterval = 5
        guard let (data, _) = try? await URLSession.shared.data(for: request),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return nil }
        return (json["models"] as? [[String: Any]] ?? []).compactMap { $0["name"] as? String }
    }

    static func check(_ done: @escaping (OllamaStatus) -> Void) {
        Task {
            let found = await models()
            await MainActor.run { done(found.map { .ready($0) } ?? .missing) }
        }
    }

    struct Failure: LocalizedError {
        let errorDescription: String?
    }

    private static func post(_ path: String, _ body: [String: Any]) -> URLRequest {
        var request = URLRequest(url: URL(string: "http://127.0.0.1:11434" + path)!)
        request.httpMethod = "POST"
        // For a stream this is the longest pause between chunks, so a stalled download fails instead of hanging.
        request.timeoutInterval = 60
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: body)
        return request
    }

    /// Downloads a model, reporting progress from 0 to 1. Ollama keeps the finished parts, so a retry resumes.
    static func pull(_ model: String, progress: @escaping @MainActor (Double) -> Void) async throws {
        let (bytes, _) = try await URLSession.shared.bytes(for: post("/api/pull", ["model": model, "stream": true]))
        var layers: [String: (total: Double, done: Double)] = [:]
        for try await line in bytes.lines {
            guard let json = try? JSONSerialization.jsonObject(with: Data(line.utf8)) as? [String: Any] else { continue }
            if let error = json["error"] as? String { throw Failure(errorDescription: error) }
            if let digest = json["digest"] as? String, let total = json["total"] as? Double, total > 0 {
                layers[digest] = (total, json["completed"] as? Double ?? 0)
                let total = layers.values.reduce(0) { $0 + $1.total }
                let done = layers.values.reduce(0) { $0 + $1.done }
                await progress(done / total)
            }
            if json["status"] as? String == "success" { return }
        }
        throw Failure(errorDescription: "Ollama")
    }

    /// Builds one of Lumi's models on top of its downloaded base: the prompt, settings and sample dialogs.
    static func create(_ recipe: LumiRecipe) async throws {
        let messages = LumiModels.examples.flatMap {
            [["role": "user", "content": $0.question], ["role": "assistant", "content": $0.answer]]
        }
        var request = post("/api/create", [
            "model": recipe.name, "from": recipe.base, "system": LumiModels.system, "messages": messages,
            "parameters": ["temperature": recipe.temperature, "num_ctx": 8192], "stream": false,
        ])
        request.timeoutInterval = 300
        let (data, _) = try await URLSession.shared.data(for: request)
        let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        if let error = json?["error"] as? String { throw Failure(errorDescription: error) }
    }
}

extension LumiRecipe {
    /// This computer's memory, in whole gigabytes.
    static let memoryGB = Int((Double(ProcessInfo.processInfo.physicalMemory) / 1_073_741_824).rounded())

    /// The largest model that fits this computer.
    static var recommended: LumiRecipe {
        LumiModels.all.last { $0.memoryGB <= memoryGB } ?? LumiModels.all[0]
    }

    var size: String { String(format: "%.1f", sizeGB) }

    func isInstalled(in models: [String]) -> Bool {
        models.contains(name) || models.contains(name + ":latest")
    }
}

/// Where the download and setup of one of Lumi's models stands.
enum LumiInstall: Equatable {
    case idle, downloading(Double), building, failed(String)
}

final class ConnectionForm: ObservableObject {
    @Published var preset: ServicePreset = .openRouter {
        didSet {
            baseURL = preset.baseURL
            if name.isEmpty || ServicePreset.allCases.contains(where: { $0.title == name }) { name = preset.title }
            if preset == .ollama { checkOllama() }
        }
    }
    @Published var ollama: OllamaStatus = .checking
    @Published var lumiRecipe = LumiRecipe.recommended
    @Published var lumiInstall = LumiInstall.idle
    private var installTask: Task<Void, Never>?

    /// Downloads the chosen model's base, builds the Lumi model on it and fills it in.
    func installLumi() {
        let recipe = lumiRecipe
        installTask?.cancel()
        lumiInstall = .downloading(0)
        installTask = Task { @MainActor in
            do {
                try await OllamaStatus.pull(recipe.base) { [weak self] in self?.lumiInstall = .downloading($0) }
                lumiInstall = .building
                try await OllamaStatus.create(recipe)
                lumiInstall = .idle
                model = recipe.name
                checkOllama()
            } catch {
                lumiInstall = .failed(error.localizedDescription)
            }
        }
    }

    func useLumi() { model = lumiRecipe.name }

    func checkOllama() {
        ollama = .checking
        OllamaStatus.check { [weak self] status in
            guard let self else { return }
            self.ollama = status
            // Fill in the first installed model so the form is ready to save.
            if case let .ready(models) = status, let first = models.first, self.model.isEmpty { self.model = first }
        }
    }
    @Published var name = ServicePreset.openRouter.title
    @Published var baseURL = ServicePreset.openRouter.baseURL
    @Published var model = ""
    @Published var key = ""

    var isValid: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty
            && URL(string: baseURL.trimmingCharacters(in: .whitespaces))?.scheme?.hasPrefix("http") == true
            && !model.trimmingCharacters(in: .whitespaces).isEmpty
    }

    func reset() {
        installTask?.cancel()
        lumiInstall = .idle
        preset = .openRouter
        name = preset.title
        model = ""
        key = ""
    }
}

struct ConnectionFormView: View {
    @ObservedObject var form: ConnectionForm
    var onCancel: () -> Void
    var onSave: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(S.formTitle).font(.system(size: 17, weight: .semibold))
            Text(S.formSubtitle)
                .font(.system(size: 12)).foregroundStyle(.secondary)

            Form {
                Picker(S.service, selection: $form.preset) {
                    ForEach(ServicePreset.allCases) { Text($0.title).tag($0) }
                }
                TextField(S.name, text: $form.name)
                TextField(S.address, text: $form.baseURL, prompt: Text("https://…/v1"))
                TextField(S.model, text: $form.model, prompt: Text(form.preset.modelHint))
                SecureField(S.apiKey, text: $form.key, prompt: Text(form.preset == .gigaChat ? S.hintGigachatKey : form.preset.needsKey ? S.keyStored : S.keyNotNeeded))
            }
            .formStyle(.columns)
            .font(.system(size: 13))

            if form.preset == .ollama { ollamaHelp }

            HStack {
                Spacer()
                Button(S.cancel, action: onCancel).keyboardShortcut(.cancelAction)
                Button(S.connect, action: onSave)
                    .keyboardShortcut(.defaultAction)
                    .disabled(!form.isValid)
            }
        }
        .padding(22)
        .frame(width: 400)
    }

    /// Under the Ollama preset: whether it's installed, with a download link and the first command if not.
    @ViewBuilder private var ollamaHelp: some View {
        VStack(alignment: .leading, spacing: 6) {
            switch form.ollama {
            case .checking:
                HStack(spacing: 6) { ProgressView().controlSize(.mini); Text("Ollama…").foregroundStyle(.secondary) }
            case .missing:
                Text(S.ollamaMissing).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Link(S.ollamaDownload, destination: OllamaStatus.downloadPage)
                        .buttonStyle(.borderedProminent).tint(.lumi).controlSize(.small)
                    Button(S.checkAgain, action: form.checkOllama).controlSize(.small)
                }
            case let .ready(models):
                Label(S.ollamaReady(String(models.count)), systemImage: "checkmark.circle.fill").foregroundStyle(Color.lumi)
                lumiSetup(models)
                if !models.isEmpty {
                    Divider().padding(.vertical, 2)
                    Text(S.ollamaPick).foregroundStyle(.secondary)
                    // The exact installed names, so the model field can't hold a name Ollama doesn't have.
                    FlowButtons(items: models, selected: form.model) { form.model = $0 }
                }
            }
        }
        .font(.system(size: 12))
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.05)))
    }

    /// Lumi's own models: pick a size, then download and set it up in one press.
    @ViewBuilder private func lumiSetup(_ models: [String]) -> some View {
        let recipe = form.lumiRecipe
        let busy = form.lumiInstall != .idle && { if case .failed = form.lumiInstall { false } else { true } }()
        Text(S.lumiOwn).fixedSize(horizontal: false, vertical: true)
        Picker("", selection: $form.lumiRecipe) {
            ForEach(LumiModels.all, id: \.self) { Text($0.title).tag($0) }
        }
        .pickerStyle(.segmented).labelsHidden().disabled(busy)
        Text(S.lumiFit(recipe.title, String(recipe.memoryGB), String(LumiRecipe.memoryGB))
             + (recipe.memoryGB > LumiRecipe.memoryGB ? " " + S.lumiSlow : ""))
            .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        switch form.lumiInstall {
        case let .downloading(part):
            ProgressView(value: part) { Text(S.lumiDownloading(recipe.title, String(Int(part * 100)))) }
                .tint(.lumi)
        case .building:
            HStack(spacing: 6) { ProgressView().controlSize(.mini); Text(S.lumiBuilding(recipe.title)) }
        case .idle, .failed:
            if case let .failed(reason) = form.lumiInstall {
                Text(S.lumiFailed(reason)).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
            }
            if recipe.isInstalled(in: models) {
                Button(S.lumiUse(recipe.title), action: form.useLumi).controlSize(.small)
            } else {
                Button(S.lumiInstall(recipe.title, recipe.size), action: form.installLumi)
                    .buttonStyle(.borderedProminent).tint(.lumi).controlSize(.small)
            }
        }
    }
}

struct WelcomeView: View {
    @ObservedObject var model: WelcomeModel
    @ObservedObject var settings = Settings.shared
    @ObservedObject var updates = UpdateChecker.shared

    var body: some View {
        VStack(spacing: 0) {
            MascotView(isWalking: model.isMascotWalking)
                .frame(width: 84, height: 72)
                .onHover { model.isMascotWalking = $0 }
                .padding(.top, 34)

            Text("Lumi")
                .font(.system(size: 26, weight: .bold, design: .rounded))
                .padding(.top, 14)
            Text(S.welcomeSubtitle)
                .font(.system(size: 13))
                .foregroundStyle(.secondary)
                .padding(.top, 4)

            HStack(spacing: 6) {
                KeyCap("⌥")
                KeyCap(hotkeyKey)
                Text(S.hotkeyHint)
                    .font(.system(size: 13))
                    .foregroundStyle(.secondary)
            }
            .padding(.top, 22)

            if let version = updates.newVersion {
                Link(destination: UpdateChecker.releasesPage) {
                    Label(S.updateAvailable(version), systemImage: "arrow.down.circle.fill")
                }
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(Color.lumi)
                .padding(.top, 14)
            }

            VStack(spacing: 0) {
                authRow
                Divider().padding(.leading, 14)
                HStack {
                    Text(S.model)
                    Spacer()
                    ModelPicker()
                }
                .padding(.horizontal, 14).padding(.vertical, 10)
                Divider().padding(.leading, 14)
                HStack {
                    Text(S.language)
                    Spacer()
                    LanguagePicker()
                }
                .padding(.horizontal, 14).padding(.vertical, 6)
                ForEach(settings.connections) { c in
                    Divider().padding(.leading, 14)
                    HStack(spacing: 8) {
                        Image(systemName: "link").foregroundStyle(.secondary)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(c.name)
                            Text(c.model).font(.system(size: 11)).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button {
                            settings.remove(c)
                        } label: {
                            Image(systemName: "trash")
                        }
                        .buttonStyle(.borderless)
                        .help(S.deleteConnection)
                    }
                    .padding(.horizontal, 14).padding(.vertical, 8)
                }
                Divider().padding(.leading, 14)
                HStack {
                    Button(S.connectModel, action: model.startAdding)
                        .buttonStyle(.borderless)
                        .foregroundStyle(Color.lumi)
                    Spacer()
                }
                .padding(.horizontal, 14).padding(.vertical, 10)
                if !settings.connections.contains(where: { OllamaStatus.isOllama($0.baseURL) }) {
                    Divider().padding(.leading, 14)
                    HStack(spacing: 8) {
                        Image(systemName: "airplane").foregroundStyle(.secondary)
                        Button(S.ollamaOffer, action: model.startAddingOllama)
                            .buttonStyle(.borderless)
                            .foregroundStyle(Color.lumi)
                        Spacer()
                    }
                    .padding(.horizontal, 14).padding(.vertical, 10)
                }
                Divider().padding(.leading, 14)
                Toggle(S.launchAtLogin, isOn: Binding(
                    get: { model.launchesAtLogin },
                    set: { model.setLaunchesAtLogin($0) }
                ))
                .padding(.horizontal, 14).padding(.vertical, 10)
                Divider().padding(.leading, 14)
                Toggle(S.showOnLaunch, isOn: $model.showsOnLaunch)
                    .padding(.horizontal, 14).padding(.vertical, 10)
                Divider().padding(.leading, 14)
                Toggle(S.showChatgpt, isOn: $settings.showsChatGPT)
                    .padding(.horizontal, 14).padding(.vertical, 10)
            }
            .toggleStyle(.switch)
            .controlSize(.small)
            .font(.system(size: 13))
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
            .padding(.horizontal, 24)
            .padding(.top, 22)

            Spacer(minLength: 22)

            Button(action: model.openSearch) {
                Text(S.openSearch)
                    .font(.system(size: 14, weight: .semibold))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 6)
            }
            .buttonStyle(.borderedProminent)
            .tint(.lumi)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)
            .padding(.horizontal, 24)

            HStack {
                Button(S.quitApp) { NSApp.terminate(nil) }
                Spacer()
                Button(S.uninstall) { Uninstaller.confirmAndRun() }
            }
            .buttonStyle(.borderless)
            .font(.system(size: 12))
            .foregroundStyle(.secondary)
            .padding(.horizontal, 26)
            .padding(.top, 12)
            .padding(.bottom, 18)
        }
        .frame(width: 400)
        .id(settings.language)
        .frame(minHeight: 560)
        .fixedSize(horizontal: false, vertical: true)
        .background(VisualEffect())
        .sheet(isPresented: $model.isAddingConnection) {
            ConnectionFormView(form: model.form, onCancel: { model.isAddingConnection = false }, onSave: model.saveConnection)
        }
        .onAppear { model.checkAuth() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            model.checkAuth()
        }
    }

    @ViewBuilder private var authRow: some View {
        HStack(spacing: 8) {
            switch model.auth {
            case .checking:
                ProgressView().controlSize(.mini)
                Text(S.authChecking).foregroundStyle(.secondary)
                Spacer()
            case .signedIn:
                Circle().fill(Color.green).frame(width: 8, height: 8)
                Text(S.authOk)
                Spacer()
            case .signedOut:
                Circle().fill(Color.orange).frame(width: 8, height: 8)
                Text(S.authSignedOut)
                Spacer()
                Button(S.signIn, action: model.signIn).controlSize(.small)
            case .missing:
                Circle().fill(Color.red).frame(width: 8, height: 8)
                Text(S.authMissing)
                Spacer()
                Link(S.download, destination: URL(string: "https://claude.com/claude-code")!)
            }
        }
        .font(.system(size: 13))
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}

/// A wrapping row of small buttons, one per item; the selected one is tinted.
struct FlowButtons: View {
    let items: [String]
    let selected: String
    let onPick: (String) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            ForEach(items, id: \.self) { item in
                Button(item) { onPick(item) }
                    .buttonStyle(.bordered)
                    .controlSize(.small)
                    .tint(item == selected ? .lumi : nil)
            }
        }
    }
}

struct KeyCap: View {
    let label: String
    init(_ label: String) { self.label = label }

    var body: some View {
        Text(label)
            .font(.system(size: 13, weight: .medium, design: .rounded))
            .frame(minWidth: 26, minHeight: 24)
            .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.primary.opacity(0.08)))
            .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Color.primary.opacity(0.15)))
    }
}

// MARK: - App

/// Appends a line to ~/Library/Logs/Lumi.log.
func debugLog(_ line: String) {
    let url = URL(fileURLWithPath: NSHomeDirectory() + "/Library/Logs/Lumi.log")
    let data = Data("\(Date()) \(line)\n".utf8)
    if let h = try? FileHandle(forWritingTo: url) {
        h.seekToEndOfFile()
        h.write(data)
        try? h.close()
    } else {
        try? data.write(to: url)
    }
}

/// Removes Lumi completely: login item, saved keys, settings, and the app itself (to the Trash, so it can be restored).
enum Uninstaller {
    static func confirmAndRun() {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = S.uninstallTitle
        alert.informativeText = S.uninstallText
        alert.alertStyle = .warning
        alert.addButton(withTitle: S.uninstallButton)
        alert.addButton(withTitle: S.cancel)
        alert.buttons.first?.hasDestructiveAction = true
        guard alert.runModal() == .alertFirstButtonReturn else { return }

        try? SMAppService.mainApp.unregister()
        for connection in Settings.shared.connections { Keychain.delete(connection.id) }
        if let domain = Bundle.main.bundleIdentifier { UserDefaults.standard.removePersistentDomain(forName: domain) }
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        try? FileManager.default.removeItem(at: support.appendingPathComponent("Lumi"))
        // A running app may move its own bundle; the process keeps running from memory until it quits.
        NSWorkspace.shared.recycle([Bundle.main.bundleURL]) { _, _ in
            DispatchQueue.main.async { NSApp.terminate(nil) }
        }
    }
}

/// Asks GitHub once a day whether a newer release is out; the menu bar menu and the welcome window then offer it.
final class UpdateChecker: ObservableObject {
    static let shared = UpdateChecker()
    static let releasesPage = URL(string: "https://github.com/AlbertS15/Lumi/releases/latest")!
    private static let latestRelease = URL(string: "https://api.github.com/repos/AlbertS15/Lumi/releases/latest")!

    @Published private(set) var newVersion: String?
    var onChange: (() -> Void)?
    private var timer: Timer?

    func start() {
        let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        // Builds made from source without a release tag have no real version to compare.
        guard !current.isEmpty, !current.hasPrefix("0.") else { return }
        check(current)
        timer = Timer.scheduledTimer(withTimeInterval: 24 * 3600, repeats: true) { [weak self] _ in self?.check(current) }
    }

    private func check(_ current: String) {
        var request = URLRequest(url: Self.latestRelease)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        URLSession.shared.dataTask(with: request) { [weak self] data, _, _ in
            guard let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let tag = json["tag_name"] as? String else { return }
            let latest = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
            guard Self.isNewer(latest, than: current) else { return }
            DispatchQueue.main.async {
                guard self?.newVersion != latest else { return }
                self?.newVersion = latest
                self?.onChange?()
            }
        }.resume()
    }

    /// Compares dotted version numbers: 1.10.0 is newer than 1.9.2.
    static func isNewer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }
        let y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0, r = i < y.count ? y[i] : 0
            if l != r { return l > r }
        }
        return false
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var panel: LauncherPanel!
    private let model = LauncherModel()
    private var statusItem: NSStatusItem!
    private var hotKey: EventHotKeyRef?
    private var isoHotKey: EventHotKeyRef?
    private var monitor: Any?
    private var hiddenAt = Date.distantPast
    private let welcome = WelcomeModel()
    private var welcomeWindow: NSWindow?
    private var loginItem: NSMenuItem?
    private var languageWatch: AnyCancellable?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        Self.retireClaudeLight()
        buildMainMenu()
        buildPanel()
        buildMenu()
        registerHotKey()

        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let panel = self?.panel, event.window === panel else { return event }
            return panel.handle(event) ? nil : event
        }
        model.hide = { [weak self] in self?.hidePanel() }
        welcome.openSearch = { [weak self] in
            self?.welcomeWindow?.close()
            self?.showPanel()
        }
        welcome.onLoginItemChange = { [weak self] in self?.syncLoginItem() }
        NotificationCenter.default.addObserver(self, selector: #selector(addConnection), name: .addConnection, object: nil)
        UpdateChecker.shared.onChange = { [weak self] in self?.rebuildMenu() }
        UpdateChecker.shared.start()

        if welcome.showsOnLaunch && !Self.launchedAsLoginItem() { showWelcome() }
    }

    /// Opening the app again while it runs (Finder, Launchpad, Spotlight) brings the welcome window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showWelcome()
        return false
    }

    private static func launchedAsLoginItem() -> Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        return event.eventID == kAEOpenApplication
            && event.paramDescriptor(forKeyword: keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
    }

    @objc func showWelcome() {
        if welcomeWindow == nil {
            let window = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 400, height: 520),
                styleMask: [.titled, .closable, .fullSizeContentView],
                backing: .buffered,
                defer: false
            )
            window.titlebarAppearsTransparent = true
            window.titleVisibility = .hidden
            window.isMovableByWindowBackground = true
            window.isReleasedWhenClosed = false
            let host = NSHostingController(rootView: WelcomeView(model: welcome))
            host.sizingOptions = [.preferredContentSize]
            window.contentViewController = host
            window.center()
            welcomeWindow = window
        }
        NSApp.activate(ignoringOtherApps: true)
        welcomeWindow?.makeKeyAndOrderFront(nil)
    }

    /// Rebuilds the model submenu each time it opens: Claude models, then connections, ticking the current one.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let settings = Settings.shared
        func add(_ title: String, _ choice: Choice) {
            let item = NSMenuItem(title: title, action: #selector(pickModel(_:)), keyEquivalent: "")
            item.representedObject = choice.stored
            item.target = self
            item.state = settings.choice == choice ? .on : .off
            menu.addItem(item)
        }
        for m in ClaudeModel.allCases { add("\(m.title) — \(m.note)", .claude(m)) }
        menu.addItem(.separator())
        menu.addItem(.separator())
        add("\(S.codexTitle) — \(S.noteCodex)", .codex)
        menu.addItem(.separator())
        for m in GeminiModel.allCases { add("\(m.title) — \(m.note)", .gemini(m)) }
        if !settings.connections.isEmpty {
            menu.addItem(.separator())
            for c in settings.connections { add("\(c.name) — \(c.model)", .custom(c.id)) }
        }
        menu.addItem(.separator())
        let item = NSMenuItem(title: S.connectModel, action: #selector(addConnection), keyEquivalent: "")
        item.target = self
        menu.addItem(item)
    }

    private func syncLoginItem() {
        loginItem?.state = SMAppService.mainApp.status == .enabled ? .on : .off
        welcome.launchesAtLogin = SMAppService.mainApp.status == .enabled
    }

    private func buildPanel() {
        panel = LauncherPanel(
            contentRect: NSRect(x: 0, y: 0, width: 720, height: 58),
            styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        panel.model = model
        panel.isFloatingPanel = true
        panel.level = .modalPanel
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.isMovableByWindowBackground = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]

        // The controller resizes the window to the SwiftUI content; resized() then pins the top edge.
        let host = NSHostingController(rootView: LauncherView(model: model))
        host.sizingOptions = [.preferredContentSize]
        panel.contentViewController = host
        host.view.wantsLayer = true
        host.view.layer?.backgroundColor = .clear
        host.view.layer?.cornerRadius = 26
        host.view.layer?.cornerCurve = .continuous
        host.view.layer?.masksToBounds = true
        panel.setContentSize(NSSize(width: 720, height: 58))
        NotificationCenter.default.addObserver(self, selector: #selector(resized), name: NSWindow.didResizeNotification, object: panel)
        NotificationCenter.default.addObserver(self, selector: #selector(panelHidden), name: NSWindow.didResignKeyNotification, object: panel)
    }

    /// Keeps the top edge fixed as results and answers grow the panel downward.
    @objc private func resized() {
        guard let screen = panel.screen ?? NSScreen.main else { return }
        var size = panel.frame.size
        if size.width < 100 || size.height < 58 { size = NSSize(width: 720, height: 58) }
        let top = screen.visibleFrame.maxY - screen.visibleFrame.height * 0.22
        let x = screen.visibleFrame.midX - size.width / 2
        let frame = NSRect(x: x, y: top - size.height, width: size.width, height: size.height)
        if panel.frame != frame {
            panel.setFrame(frame, display: true)
            panel.invalidateShadow()
        }
    }

    @objc private func panelHidden() { hiddenAt = Date() }

    private func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = Self.sparkImage()
        rebuildMenu()
        // Published fires before the value is stored, so rebuild on the next turn of the run loop.
        languageWatch = Settings.shared.$language.dropFirst().sink { [weak self] _ in
            DispatchQueue.main.async { self?.rebuildMenu() }
        }
    }

    @objc private func uninstall() { Uninstaller.confirmAndRun() }

    @objc private func openReleases() { NSWorkspace.shared.open(UpdateChecker.releasesPage) }

    /// Lumi used to be called ClaudeLight (same bundle id, so settings carry over). Two copies would both
    /// grab the hotkey, so the old one is quit and its app moved to the Trash, where it can be restored.
    private static func retireClaudeLight() {
        let me = Bundle.main.bundleURL.standardizedFileURL
        for app in NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "")
        where app.bundleURL?.standardizedFileURL != me {
            app.terminate()
        }
        let old = ["/Applications/ClaudeLight.app", NSHomeDirectory() + "/Applications/ClaudeLight.app"]
            .map { URL(fileURLWithPath: $0) }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
        if !old.isEmpty {
            // Give the old process a moment to quit before its bundle moves.
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { NSWorkspace.shared.recycle(old) }
        }
    }

    /// An app without a Dock icon has no menu bar of its own; this hidden one makes ⌘Q, ⌘C and ⌘V work in its windows.
    private func buildMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: S.quitApp, action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)
        let editItem = NSMenuItem()
        let editMenu = NSMenu(title: "Edit")
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = editMenu
        main.addItem(editItem)
        NSApp.mainMenu = main
    }

    private func rebuildMenu() {
        let menu = NSMenu()
        if let version = UpdateChecker.shared.newVersion {
            menu.addItem(withTitle: "⬆︎ " + S.updateAvailable(version), action: #selector(openReleases), keyEquivalent: "")
            menu.addItem(.separator())
        }
        menu.addItem(withTitle: S.menuOpenSearch("⌥ " + hotkeyKey), action: #selector(togglePanel), keyEquivalent: "")
        menu.addItem(withTitle: S.menuWindow, action: #selector(showWelcome), keyEquivalent: "")
        let login = NSMenuItem(title: S.menuLaunchAtLogin, action: #selector(toggleLogin(_:)), keyEquivalent: "")
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        loginItem = login
        let models = NSMenu()
        models.delegate = self
        let modelItem = NSMenuItem(title: S.model, action: nil, keyEquivalent: "")
        modelItem.submenu = models
        menu.addItem(modelItem)
        menu.addItem(.separator())
        menu.addItem(withTitle: S.uninstall, action: #selector(uninstall), keyEquivalent: "")
        menu.addItem(withTitle: S.menuQuit, action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        for item in menu.items where item.action != #selector(NSApplication.terminate(_:)) { item.target = self }
        statusItem.menu = menu
    }

    /// The menu bar Lumi, a template image (eyes cut out) so it follows the menu bar's colour.
    static func sparkImage() -> NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 14), flipped: true) { _ in
            NSColor.black.setFill()
            for p in Mascot.pixels(withHat: false) where !p.isEye {
                NSRect(x: 2 + CGFloat(p.x), y: 1 + CGFloat(p.y), width: 1, height: 1).fill()
            }
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "Lumi"
        return image
    }

    @objc private func pickModel(_ item: NSMenuItem) {
        Settings.shared.choice = Choice(stored: item.representedObject as? String ?? "")
    }

    @objc private func addConnection() {
        showWelcome()
        welcome.startAdding()
    }

    @objc private func toggleLogin(_ item: NSMenuItem) {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            NSSound.beep()
        }
        syncLoginItem()
    }

    private func registerHotKey() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, data in
            guard let data else { return noErr }
            let delegate = Unmanaged<AppDelegate>.fromOpaque(data).takeUnretainedValue()
            DispatchQueue.main.async { delegate.togglePanel() }
            return noErr
        }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), nil)

        let id = EventHotKeyID(signature: OSType(0x434C4C54), id: 1) // 'CLLT'
        // Option + the key left of 1: ANSI keyboards report it as Grave, ISO (European) ones as Section.
        let status = RegisterEventHotKey(UInt32(kVK_ANSI_Grave), UInt32(optionKey), id, GetApplicationEventTarget(), 0, &hotKey)
        let isoStatus = RegisterEventHotKey(UInt32(kVK_ISO_Section), UInt32(optionKey), id, GetApplicationEventTarget(), 0, &isoHotKey)
        debugLog("hotkey registered, status \(status), iso \(isoStatus)")
    }

    @objc func togglePanel() {
        debugLog("toggle, visible=\(panel.isVisible)")
        panel.isVisible ? hidePanel() : showPanel()
    }

    private func showPanel() {
        // A fresh start after a few minutes away, like Spotlight; a quick reopen keeps the answer.
        if Date().timeIntervalSince(hiddenAt) > 300 { model.resetIfStale() }
        resized()
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        NotificationCenter.default.post(name: .launcherShown, object: nil)
    }

    private func hidePanel() {
        panel.orderOut(nil)
        hiddenAt = Date()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
