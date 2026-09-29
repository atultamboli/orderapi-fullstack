# OrderApi Full-Stack Deployment Plan

Goal: one live portfolio project with 2 frontends + 2 backends sharing one API contract.

```
   Angular app ─┐                 ┌─► .NET API   ─┐
                ├──► same REST ───┤               ├──► MongoDB Atlas (cloud)
   React app  ──┘    API contract └─► Python API ─┘
```

## Before Day 1 — accounts to create (use PERSONAL Gmail, never office email)

| # | Account | URL | Plan | Card? |
|---|---|---|---|---|
| 1 | Personal Gmail | gmail.com | Free | No |
| 2 | GitHub | github.com | Free | No |
| 3 | MongoDB Atlas | mongodb.com/cloud/atlas | M0 Free cluster | No |
| 4 | Render | render.com | Free (sign in with GitHub) | No |
| 5 | Vercel | vercel.com | Hobby (sign in with GitHub) | No (needed Phase 2) |
| 6 | Netlify | netlify.com | Free (sign in with GitHub) | No (needed Phase 3) |
| 7 | OpenAI API | platform.openai.com | Prepaid credits | Already have |

Check on the OpenAI account:
- A ChatGPT Plus subscription is NOT the same as API credits. The API needs
  credit under platform.openai.com → Billing. Confirm the balance there.
- Set a monthly usage limit and keep auto-recharge OFF.
- Create an API key when we reach Phase 1 step 6 — never paste it into code or chat.

Domain name: not needed. We use free URLs (*.onrender.com, *.vercel.app, *.netlify.app).

## Phase 1 — .NET API live (Day 1–3)

1. Restructure folders: move existing code into `backend-dotnet/`
2. `git init`, add `.gitignore` (bin/, obj/, .vs/, *.docx, secrets)
3. Code fixes for the cloud:
   - Make OpenAI key optional (AI endpoints return 503 instead of app crashing)
   - Add CORS for the frontends
   - Read Mongo connection string from environment variable
   - Add a `/health` endpoint
   - Add a Dockerfile (Render runs .NET via Docker)
4. Create MongoDB Atlas free cluster, DB user, allow network access
5. Push code to GitHub
6. Deploy on Render, set environment variables:
   - `Storage__MongoDb__ConnectionString`
   - `OpenAI__ApiKey`
7. Test live Swagger URL

## Phase 2 — React frontend on Vercel (Day 4–6)
Orders list, order detail, upload file, AI summary. API URL from `VITE_API_URL`.

## Phase 3 — Angular frontend on Netlify (Day 7–9)
Same screens, same API. API URL from environment file.

## Phase 4 — Python FastAPI backend on Render (Day 10–13)
Same endpoints and JSON shapes as .NET. Frontends switch backend by changing one URL.

## Phase 5 — Polish (Day 14)
README with live links, screenshots, architecture diagram → add to Upwork/LinkedIn portfolio.

## Cost
₹0 for hosting. OpenAI uses existing credits. No subscriptions.

## Rules
- Personal accounts only; no office email, code, or data.
- Secrets live only in user-secrets (local) and Render env vars (cloud).
- Ideally work from a personal laptop.
