# 💳 Stripe Setup Guide - Clienta API

## 🎯 Overview
This guide will help you set up Stripe billing with two subscription plans.

**Pricing Structure:**
- **Basic**: ₪39/month only (no yearly option to keep it simple)
- **Pro**: ₪69/month or ₪690/year (save 2 months!)

---

## 📋 Step 1: Create Stripe Products

### Go to Stripe Dashboard
👉 https://dashboard.stripe.com/products

### Create Product #1: Clienta Basic
1. Click **"Add product"**
2. Fill in:
   - **Name**: `Clienta Basic`
   - **Description**: `Basic plan with 25 users and 5,000 messages`

3. **Add Monthly Price:**
   - Click **"Add another price"**
   - **Price**: `39`
   - **Currency**: `ILS` (Israeli Shekel)
   - **Billing period**: `Monthly`
   - **📝 Copy the Price ID** (looks like `price_1Abc123DEf456GHi`)

**Note:** Basic plan only has monthly billing. No yearly option needed.

### Create Product #2: Clienta Pro
1. Click **"Add product"**
2. Fill in:
   - **Name**: `Clienta Pro`
   - **Description**: `Pro plan with unlimited users and messages`

3. **Add Monthly Price:**
   - **Price**: `69`
   - **Currency**: `ILS`
   - **Billing period**: `Monthly`
   - **📝 Copy the Price ID**

4. **Add Yearly Price:**
   - **Price**: `690`
   - **Currency**: `ILS`
   - **Billing period**: `Yearly`
   - **📝 Copy the Price ID**

---

## 🔑 Step 2: Get API Keys

### Test Mode Keys (for development)
1. Go to: https://dashboard.stripe.com/test/apikeys
2. Copy:
   - **Secret key** (starts with `sk_test_`)
   - **Publishable key** (starts with `pk_test_`)

### Live Mode Keys (for production)
⚠️ **Only after testing!**
1. Toggle to **Live mode** in Stripe dashboard
2. Go to: https://dashboard.stripe.com/apikeys
3. Copy:
   - **Secret key** (starts with `sk_live_`)
   - **Publishable key** (starts with `pk_live_`)

---

## 🔔 Step 3: Setup Webhooks

### Create Webhook Endpoint
1. Go to: https://dashboard.stripe.com/test/webhooks
2. Click **"Add endpoint"**
3. Enter your endpoint URL:
   ```
   https://yourdomain.com/api/stripe/webhook
   ```
4. Select events to listen for:
   - ✅ `checkout.session.completed`
   - ✅ `customer.subscription.updated`
   - ✅ `customer.subscription.deleted`
   - ✅ `invoice.payment_succeeded`
   - ✅ `invoice.payment_failed`

5. Click **"Add endpoint"**
6. **📝 Copy the Signing secret** (starts with `whsec_`)

---

## ⚙️ Step 4: Update Configuration

### Edit `appsettings.json`
```json
{
  "Stripe": {
    "SecretKey": "sk_test_YOUR_SECRET_KEY_HERE",
    "PublishableKey": "pk_test_YOUR_PUBLISHABLE_KEY_HERE",
    "WebhookSecret": "whsec_YOUR_WEBHOOK_SECRET_HERE",
    "BasicMonthlyPriceId": "price_YOUR_BASIC_MONTHLY_ID",
    "ProMonthlyPriceId": "price_YOUR_PRO_MONTHLY_ID",
    "ProYearlyPriceId": "price_YOUR_PRO_YEARLY_ID"
  }
}
```

**Note:** Only 3 price IDs needed. Basic plan is monthly-only.

**Important:** Never commit real API keys to git! Use environment variables in production.

---

## 🧪 Step 5: Test the Integration

### Test Checkout Flow
```bash
# 1. Get checkout URL for Basic plan
curl -X POST https://localhost:5146/api/billing/upgrade \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planType": 1}'

# 2. Open the returned URL in browser
# 3. Use Stripe test card: 4242 4242 4242 4242
#    - Expiry: Any future date
#    - CVC: Any 3 digits
```

### Test Webhook Locally
```bash
# Install Stripe CLI
stripe login

# Forward webhooks to local server
stripe listen --forward-to http://localhost:5146/api/stripe/webhook

# Trigger test event
stripe trigger checkout.session.completed
```

---

## 📊 API Endpoints

### Upgrade to Basic (Monthly only)
```http
POST /api/billing/upgrade
Content-Type: application/json
Authorization: Bearer {jwt_token}

{
  "planType": 1,       // 1 = Basic
  "billingCycle": 0    // 0 = Monthly (yearly not available for Basic)
}

Response:
{
  "url": "https://checkout.stripe.com/c/pay/cs_test_..."
}
```

### Upgrade to Basic Yearly - NOT AVAILABLE
```http
// This will return an error:
// "Yearly billing not available for Basic plan. Upgrade to Pro for yearly billing."
```

### Upgrade to Pro Monthly
```http
POST /api/billing/upgrade
Content-Type: application/json
Authorization: Bearer {jwt_token}

{
  "planType": 2,       // 2 = Pro
  "billingCycle": 0    // 0 = Monthly
}
```

### Upgrade to Pro Yearly (Save ₪138!)
```http
POST /api/billing/upgrade
Content-Type: application/json
Authorization: Bearer {jwt_token}

{
  "planType": 2,       // 2 = Pro
  "billingCycle": 1    // 1 = Yearly
}
```

### Change Existing Plan (Upgrade/Downgrade)
```http
POST /api/billing/change-plan
Content-Type: application/json
Authorization: Bearer {jwt_token}

{
  "newPlan": 2,        // 1 = Basic, 2 = Pro
  "newCycle": 1        // 0 = Monthly, 1 = Yearly
}

Response:
{
  "success": true,
  "message": "Plan changed successfully. Proration will be applied on next invoice.",
  "newPlan": "Pro",
  "newCycle": "Yearly"
}
```

**Important:** Use `/change-plan` for existing subscribers. Stripe will automatically:
- Calculate proration
- Update subscription (no cancellation!)
- Apply credit/charge on next invoice

### Get Billing Status
```http
GET /api/billing/status
Authorization: Bearer {jwt_token}

Response:
{
  "plan": "Basic",
  "billingCycle": "Yearly",
  "trialEndsAt": null,
  "userLimit": 25,
  "messageLimit": 5000,
  "isSuspended": false,
  "stripeCustomerId": "cus_xxxxx",
  "features": {
    "maxUsers": 25,
    "maxMessages": 5000,
    "customBranding": true
  }
}
```

---

## 🎭 Test Cards

### Successful Payment
- **Card**: `4242 4242 4242 4242`
- **Expiry**: Any future date
- **CVC**: Any 3 digits

### Payment Declined
- **Card**: `4000 0000 0000 0002`

### Requires Authentication (3D Secure)
- **Card**: `4000 0025 0000 3155`

More test cards: https://stripe.com/docs/testing

---

## 🚨 Grace Period Logic

When a payment fails:
1. ✅ **Day 0**: Payment fails → Grace period starts (3 days)
2. ✅ **Day 1-3**: Account remains active, email reminders sent
3. ❌ **Day 4**: Account suspended if still unpaid
4. ✅ **Payment succeeds**: Grace period cleared, account unsuspended

This approach **increases retention** significantly!

---

## 🔐 Security Best Practices

1. **Never expose Secret Key**
   - Keep in environment variables
   - Never commit to git
   - Use different keys for test/live

2. **Verify Webhook Signatures**
   - Already implemented in `StripeService.HandleWebhookAsync()`
   - Uses `WebhookSecret` to verify authenticity

3. **Use HTTPS in Production**
   - Stripe requires HTTPS for webhooks
   - Use SSL certificate (Let's Encrypt is free)

4. **Monitor Failed Payments**
   - Check Stripe Dashboard regularly
   - Set up email alerts for failed payments

---

## 📚 Resources

- Stripe Dashboard: https://dashboard.stripe.com
- Stripe Docs: https://stripe.com/docs
- Stripe CLI: https://stripe.com/docs/stripe-cli
- Test Cards: https://stripe.com/docs/testing

---

## ✅ Checklist

- [ ] Created Basic product with 1 price (₪39/month)
- [ ] Created Pro product with 2 prices (₪69/month, ₪690/year)
- [ ] Copied 3 Price IDs to `appsettings.json`
- [ ] Copied API Keys to `appsettings.json`
- [ ] Created webhook endpoint
- [ ] Copied Webhook Secret to `appsettings.json`
- [ ] Tested Basic monthly checkout with test card
- [ ] Tested Pro monthly checkout with test card
- [ ] Tested Pro yearly checkout with test card
- [ ] Verified Basic yearly returns error
- [ ] Tested webhook with Stripe CLI
- [ ] Verified grace period logic works
- [ ] Ready for production! 🚀
