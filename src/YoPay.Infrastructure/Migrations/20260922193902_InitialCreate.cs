using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoPay.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "yopay");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fraud_signals",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    trx_id_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    first_seen_merchant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fraud_signals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "merchants",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    contact_msisdn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merchants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parser_templates",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    sender_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pattern = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    field_map_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_credit = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    base_confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    hit_count = table.Column<long>(type: "bigint", nullable: false),
                    last_hit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parser_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "request_nonces",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nonce = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_nonces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_credentials",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hmac_secret_encrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_credentials_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalSchema: "yopay",
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    grace_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    monthly_event_allowance = table.Column<int>(type: "integer", nullable: false),
                    max_wallets = table.Column<int>(type: "integer", nullable: false),
                    max_devices = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalSchema: "yopay",
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    declared_monthly_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.ForeignKey(
                        name: "fk_wallets_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalSchema: "yopay",
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoints", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_merchants_merchant_id",
                        column: x => x.merchant_id,
                        principalSchema: "yopay",
                        principalTable: "merchants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    public_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    app_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    permission_state = table.Column<int>(type: "integer", nullable: false),
                    battery_percent = table.Column<int>(type: "integer", nullable: true),
                    network_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_devices_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalSchema: "yopay",
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    charged_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    grace_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    customer_msisdn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    redirect_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    callback_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoices_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalSchema: "yopay",
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    signature = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    response_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_webhook_endpoints_webhook_endpoint_id",
                        column: x => x.webhook_endpoint_id,
                        principalSchema: "yopay",
                        principalTable: "webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_events",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    sender_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    device_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    server_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dedupe_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_raw_events_devices_device_id",
                        column: x => x.device_id,
                        principalSchema: "yopay",
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_sessions",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_sessions_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "yopay",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payment_sessions_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalSchema: "yopay",
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parsed_transactions",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    trx_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sender_msisdn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    balance_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parsed_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_parsed_transactions_parser_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "yopay",
                        principalTable: "parser_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_parsed_transactions_raw_events_raw_event_id",
                        column: x => x.raw_event_id,
                        principalSchema: "yopay",
                        principalTable: "raw_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_matches",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parsed_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategy = table.Column<int>(type: "integer", nullable: false),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    operator_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_matches_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "yopay",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_matches_parsed_transactions_parsed_transaction_id",
                        column: x => x.parsed_transaction_id,
                        principalSchema: "yopay",
                        principalTable: "parsed_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_credentials_key_id",
                schema: "yopay",
                table: "api_credentials",
                column: "key_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_credentials_merchant_id_key_version",
                schema: "yopay",
                table: "api_credentials",
                columns: new[] { "merchant_id", "key_version" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                schema: "yopay",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_occurred_at",
                schema: "yopay",
                table: "audit_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_devices_fingerprint",
                schema: "yopay",
                table: "devices",
                column: "fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_devices_last_heartbeat_at",
                schema: "yopay",
                table: "devices",
                column: "last_heartbeat_at");

            migrationBuilder.CreateIndex(
                name: "ix_devices_wallet_id",
                schema: "yopay",
                table: "devices",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_fraud_signals_method_trx_id_hash",
                schema: "yopay",
                table: "fraud_signals",
                columns: new[] { "method", "trx_id_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_merchant_id_order_ref",
                schema: "yopay",
                table: "invoices",
                columns: new[] { "merchant_id", "order_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status_grace_until",
                schema: "yopay",
                table: "invoices",
                columns: new[] { "status", "grace_until" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_wallet_id",
                schema: "yopay",
                table: "invoices",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_merchants_slug",
                schema: "yopay",
                table: "merchants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_next_attempt_at",
                schema: "yopay",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parsed_transactions_method_trx_id",
                schema: "yopay",
                table: "parsed_transactions",
                columns: new[] { "method", "trx_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parsed_transactions_raw_event_id",
                schema: "yopay",
                table: "parsed_transactions",
                column: "raw_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_parsed_transactions_template_id",
                schema: "yopay",
                table: "parsed_transactions",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_parsed_transactions_wallet_id_amount_occurred_at",
                schema: "yopay",
                table: "parsed_transactions",
                columns: new[] { "wallet_id", "amount", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parser_templates_method_sender_id_is_active",
                schema: "yopay",
                table: "parser_templates",
                columns: new[] { "method", "sender_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_parser_templates_method_sender_id_version",
                schema: "yopay",
                table: "parser_templates",
                columns: new[] { "method", "sender_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_matches_invoice_id",
                schema: "yopay",
                table: "payment_matches",
                column: "invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_matches_parsed_transaction_id",
                schema: "yopay",
                table: "payment_matches",
                column: "parsed_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_sessions_invoice_id",
                schema: "yopay",
                table: "payment_sessions",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_sessions_wallet_id_expected_amount",
                schema: "yopay",
                table: "payment_sessions",
                columns: new[] { "wallet_id", "expected_amount" },
                unique: true,
                filter: "state = 1");

            migrationBuilder.CreateIndex(
                name: "ix_payment_sessions_wallet_id_state_expires_at",
                schema: "yopay",
                table: "payment_sessions",
                columns: new[] { "wallet_id", "state", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_raw_events_dedupe_hash",
                schema: "yopay",
                table: "raw_events",
                column: "dedupe_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_events_device_id",
                schema: "yopay",
                table: "raw_events",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_events_device_received_at",
                schema: "yopay",
                table: "raw_events",
                column: "device_received_at");

            migrationBuilder.CreateIndex(
                name: "ix_raw_events_server_received_at",
                schema: "yopay",
                table: "raw_events",
                column: "server_received_at")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "ix_raw_events_state_server_received_at",
                schema: "yopay",
                table: "raw_events",
                columns: new[] { "state", "server_received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_request_nonces_expires_at",
                schema: "yopay",
                table: "request_nonces",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_request_nonces_key_id_nonce",
                schema: "yopay",
                table: "request_nonces",
                columns: new[] { "key_id", "nonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_merchant_id_status",
                schema: "yopay",
                table: "subscriptions",
                columns: new[] { "merchant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_wallets_merchant_id_method_number",
                schema: "yopay",
                table: "wallets",
                columns: new[] { "merchant_id", "method", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_invoice_id",
                schema: "yopay",
                table: "webhook_deliveries",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_status_next_retry_at",
                schema: "yopay",
                table: "webhook_deliveries",
                columns: new[] { "status", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_webhook_endpoint_id",
                schema: "yopay",
                table: "webhook_deliveries",
                column: "webhook_endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_merchant_id_is_active",
                schema: "yopay",
                table: "webhook_endpoints",
                columns: new[] { "merchant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_credentials",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "fraud_signals",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "payment_matches",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "payment_sessions",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "request_nonces",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "parsed_transactions",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "webhook_endpoints",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "parser_templates",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "raw_events",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "yopay");

            migrationBuilder.DropTable(
                name: "merchants",
                schema: "yopay");
        }
    }
}
