# vulninfra
For finding secrets, tokens and other common mistakes made by developers. Any automated code scanner should be able to pick the secrets within this repo.
> Note that all content in this repo contains just things for demo purposes only. 

All of these code samples have been picked from various repositories across GitHub itself, so they represent simulate real world code samples. All these secrets within the repo, e.g. API keys, private keys, etc have been modified slightly to render them invalid while retaining their valid structure according to the docs.

Contents:
- `accesskeys`, `cert.pem` - Contains private key.
- `app.js` - Contains GitHub tokens.
- `jenkisfile` - Contains SonarQube Docs creds.
- `run.js` - Contains mongodb creds.
- `services.json` - Contains Google Cloud API key, OAuth Secrets.
- `clientcs.cs` - Contains internal database creds.
- `.env` - Environment file containing database passwords, secrets.