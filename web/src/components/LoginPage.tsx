import { useState } from "react";
import { login, register } from "../api";
import type { AuthResponse } from "../types";

interface Props {
  onLoggedIn: (auth: AuthResponse) => void;
}

export default function LoginPage({ onLoggedIn }: Props) {
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const auth = mode === "login" ? await login(email, password) : await register(email, password);
      onLoggedIn(auth);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  const isRegister = mode === "register";

  return (
    <div className="login-page">
      <form
        className="login-card"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <div className="brand">
          <span className="brand-mark">◆</span>
          <div>
            <h1>Document Assistant</h1>
            <p>Ask questions about your PDFs</p>
          </div>
        </div>

        <h2>{isRegister ? "Create an account" : "Log in"}</h2>

        <label className="field">
          <span>Email</span>
          <input
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </label>

        <label className="field">
          <span>Password</span>
          <input
            type="password"
            autoComplete={isRegister ? "new-password" : "current-password"}
            required
            minLength={isRegister ? 8 : undefined}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {isRegister && <small className="muted">At least 8 characters.</small>}
        </label>

        {error && <div className="notice notice-error">{error}</div>}

        <button type="submit" className="upload-btn" disabled={busy}>
          {busy ? "Please wait…" : isRegister ? "Create account" : "Log in"}
        </button>

        <p className="muted switch">
          {isRegister ? "Already have an account?" : "New here?"}{" "}
          <button
            type="button"
            className="link-btn"
            onClick={() => {
              setMode(isRegister ? "login" : "register");
              setError(null);
            }}
          >
            {isRegister ? "Log in" : "Create an account"}
          </button>
        </p>
      </form>
    </div>
  );
}
