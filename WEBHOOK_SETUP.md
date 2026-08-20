# Stripe Webhook Local Development Setup

## Step 1: Install Stripe CLI

### Windows Installation Options:

**Option A: Direct Download (Easiest)**
1. Go to: https://github.com/stripe/stripe-cli/releases/latest
2. Download: `stripe_X.X.X_windows_x86_64.zip`
3. Extract to: `C:\stripe\`
4. Add to PATH or use full path

**Option B: Scoop**
```bash
scoop bucket add stripe https://github.com/stripe/scoop-stripe-cli.git
scoop install stripe
```

**Option C: Chocolatey**
```bash
choco install stripe-cli
```

## Step 2: Login to Stripe Account

```bash
stripe login
```

This will:
- Open a browser window
- Ask you to authorize the CLI
- Link your Stripe test account

## Step 3: Start Your Application

Make sure your ASP.NET Core application is running:

```bash
cd "C:\AI Temp Playground\Stripe Integration\POC"
dotnet run
```

Note the port number (usually https://localhost:5001 or http://localhost:5000)

## Step 4: Forward Webhooks to Local Endpoint

Open a **new terminal** and run:

```bash
stripe listen --forward-to https://localhost:5001/api/webhook/stripe
```

**Important:** This command will output something like:
```
> Ready! Your webhook signing secret is whsec_1234567890abcdef...
```

**Copy this webhook secret!** You'll need it for the next step.

## Step 5: Configure Webhook Secret

Update `appsettings.Development.json` with the webhook secret from Step 4:

```json
{
  "Stripe": {
    "WebhookSecret": "whsec_1234567890abcdef..."
  }
}
```

Then **restart your application**.

## Step 6: Test Webhook Events

### Method 1: Trigger Test Events
In a **third terminal**, run:

```bash
# Test payment success
stripe trigger payment_intent.succeeded

# Test payment failure
stripe trigger payment_intent.payment_failed

# Test setup intent (card saving)
stripe trigger setup_intent.succeeded

# Test customer creation
stripe trigger customer.created

# Test payment method attached
stripe trigger payment_method.attached
```

### Method 2: Use Your Application UI
1. Go to http://localhost:5001/home.html
2. Make a test payment
3. Webhooks will automatically fire and be forwarded to your local endpoint

## Step 7: Monitor Webhook Events

### In Stripe CLI
The `stripe listen` terminal will show all incoming webhooks in real-time

### In Your Application Logs
Check the console output for webhook processing logs:
```
info: POC.Controllers.WebhookController[0]
      Processing webhook event: evt_123... - payment_intent.succeeded
```

### Check Azure Table Storage
View stored webhook events at:
- Endpoint: GET http://localhost:5001/api/webhook/test
- Endpoint: GET http://localhost:5001/api/webhook/config

## Testing Checklist

- [ ] Stripe CLI installed and authenticated
- [ ] Application running on localhost
- [ ] `stripe listen` forwarding webhooks
- [ ] Webhook secret configured in appsettings.Development.json
- [ ] Application restarted after configuration
- [ ] Test events triggered successfully
- [ ] Webhook logs visible in application console
- [ ] Events stored in Azure Table Storage (or local storage)

## Troubleshooting

### "Missing Stripe signature header"
- Make sure you're using the webhook secret from `stripe listen`, not from Stripe Dashboard
- Restart your application after updating the secret

### "Webhook signature validation failed"
- The webhook secret doesn't match
- Update appsettings.Development.json with the correct secret from `stripe listen`

### "Connection refused" in Stripe CLI
- Make sure your application is running
- Check the port number matches (5001 vs 5000)
- Try HTTP instead of HTTPS: `--forward-to http://localhost:5000/api/webhook/stripe`

### Events not appearing in logs
- Check that webhook handlers are implemented (WebhookService.cs)
- Look for errors in application console
- Verify the event type is in the supported list

## Useful Commands

```bash
# Check Stripe CLI version
stripe version

# List recent webhook events
stripe events list --limit 10

# View specific event
stripe events retrieve evt_123...

# Trigger specific event with custom data
stripe trigger payment_intent.succeeded --amount 2000 --currency usd

# Test webhook endpoint directly
stripe events resend evt_123...

# View webhook forwarding logs
stripe logs tail
```

## Next Steps

Once local development is working:
1. Register production webhook endpoint in Stripe Dashboard
2. Get production webhook secret
3. Configure production webhook secret in Azure App Settings
4. Deploy to Azure
5. Test production webhooks
