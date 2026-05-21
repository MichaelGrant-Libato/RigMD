# RigMD — PC Diagnostic Decision Support System

> A guided Windows desktop application that interprets user-reported PC
> performance symptoms against a system profile to deliver action-classified
> diagnostic advisory output.

**Team:** 2526-sem2-it332-11  
**Course:** IT 332  
**School Year:** AY 2025–2026, Semester 2

---

## What is RigMD?

RigMD is a downloadable Windows desktop application that helps non-technical
desktop PC owners identify probable causes of Windows-observable performance
symptoms and decide on a clearly classified next action — without needing
technical background to interpret the results.

---

## Features

- System Profile Module (manual + optional auto-detection)
- Structured OS Symptom Intake (guided session, 8+ data points)
- Internal Diagnostic Interpretation Engine (rule-based + AI-assisted)
- Advisory Action Module (Monitor / Maintain / Troubleshoot / Escalate)
- Transparent Result Dashboard with warning signs reference table
- Session History & Recurring Pattern Detection (SQLite, local storage)

---

## Tech Stack

| Layer | Technology |
|---|---|
| GUI Framework | CustomTkinter (Python) |
| Diagnostic Logic | Python rule-based engine |
| AI Explanation | Gemini API (gemini-2.0-flash) |
| Local Storage | SQLite via sqlite3 |
| System Detection | platform, wmi (Python) |
| Packaging | PyInstaller (.exe) |

---

## Getting Started

### Prerequisites
- Python 3.11 or later
- pip
- Windows 10/11 (target platform)

### Installation

```bash
git clone https://github.com/YOUR_USERNAME/rigmd.git
cd rigmd
python -m venv venv
venv\Scripts\activate       # Windows
pip install -r requirements.txt
```

### Running the app

```bash
python main.py
```

---

## Project Structure

```
rigmd/
├── assets/                  # Icons, images, UI assets
├── core/
│   ├── diagnostic_engine.py # Rule-based diagnostic logic
│   ├── advisory_module.py   # Action category classifier
│   └── ai_explainer.py      # Gemini API integration
├── database/
│   └── session_history.py   # SQLite session management
├── ui/
│   ├── profile_screen.py    # System profile module
│   ├── intake_screen.py     # Symptom intake form
│   ├── result_screen.py     # Result dashboard
│   └── history_screen.py    # Session history view
├── tests/
│   └── scenarios/           # 20 expert validation scenarios
├── main.py                  # App entry point
├── requirements.txt
└── README.md
```

---

## Team Members

| Name | GitHub | Role |
|---|---|---|
| Libato, Michael Grant | @username | |
| Macansantos, Axcelson | @username | |
| Ruperez, Raymart | @username | |
| Maestrado, Ralph Keane | @username | |
| Labaya, Godwin | @username | |

---

## Branch Strategy

- `main` — stable releases only
- `dev` — active integration branch
- `feature/[name]` — individual feature branches (merge into dev via PR)

---

## License

For academic use only — AY 2025–2026.
