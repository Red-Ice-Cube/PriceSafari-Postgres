using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PriceSafari.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    PartnerName = table.Column<string>(type: "text", nullable: false),
                    PartnerSurname = table.Column<string>(type: "text", nullable: false),
                    CodePAR = table.Column<string>(type: "text", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsMember = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AccesToViewSafari = table.Column<bool>(type: "boolean", nullable: false),
                    AccesToCreateSafari = table.Column<bool>(type: "boolean", nullable: false),
                    AccesToViewMargin = table.Column<bool>(type: "boolean", nullable: false),
                    AccesToSetMargin = table.Column<bool>(type: "boolean", nullable: false),
                    AccesToViewPriceAutomation = table.Column<bool>(type: "boolean", nullable: false),
                    AccesToEditPriceAutomation = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginDateTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LoginCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VerificationCode = table.Column<string>(type: "text", nullable: true),
                    VerificationCodeExpires = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PendingStoreNameCeneo = table.Column<string>(type: "text", nullable: true),
                    PendingStoreNameGoogle = table.Column<string>(type: "text", nullable: true),
                    PendingCeneoFeedUrl = table.Column<string>(type: "text", nullable: true),
                    PendingGoogleFeedUrl = table.Column<string>(type: "text", nullable: true),
                    CeneoFeedSubmittedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    GoogleFeedSubmittedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PendingStoreNameAllegro = table.Column<string>(type: "text", nullable: true),
                    AllegroSubmittedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CeneoFieldMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    LocalName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CeneoFieldMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    HexColor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoOfrs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OfferUrl = table.Column<string>(type: "text", nullable: true),
                    GoogleOfferUrl = table.Column<string>(type: "text", nullable: true),
                    GoogleGid = table.Column<string>(type: "text", nullable: true),
                    ProductIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    ProductIdsGoogle = table.Column<List<int>>(type: "integer[]", nullable: false),
                    IsScraped = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleIsScraped = table.Column<bool>(type: "boolean", nullable: false),
                    PricesCount = table.Column<int>(type: "integer", nullable: false),
                    GooglePricesCount = table.Column<int>(type: "integer", nullable: false),
                    IsRejected = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleIsRejected = table.Column<bool>(type: "boolean", nullable: false),
                    IsGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    CeneoSalesCount = table.Column<int>(type: "integer", nullable: true),
                    UseGPID = table.Column<bool>(type: "boolean", nullable: false),
                    UseWRGA = table.Column<bool>(type: "boolean", nullable: false),
                    CollectGoogleStoreLinks = table.Column<bool>(type: "boolean", nullable: false),
                    StoreNames = table.Column<List<string>>(type: "text[]", nullable: false),
                    StoreProfiles = table.Column<List<string>>(type: "text[]", nullable: false),
                    GoogleCid = table.Column<string>(type: "text", nullable: true),
                    GoogleHid = table.Column<string>(type: "text", nullable: true),
                    UseGoogleHidOffer = table.Column<bool>(type: "boolean", nullable: false),
                    IsAdditionalCatalog = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoOfrs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DayDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    IsOnline = table.Column<bool>(type: "boolean", nullable: false),
                    LastCheck = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UrlScalEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GooCrawEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CenCrawEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BaseScalEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ApiBotEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AleBaseScalEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UrlScalAleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AleCrawEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AleApiBotEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    InvoiceGeneratorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentProcessorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailSenderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MarketPlaceAutomationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PriceComparisonAutomationEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoogleFieldMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    LocalName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleFieldMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceCounters",
                columns: table => new
                {
                    InvoiceCounterId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastProformaNumber = table.Column<int>(type: "integer", nullable: false),
                    LastInvoiceNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCounters", x => x.InvoiceCounterId);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanName = table.Column<string>(type: "text", nullable: false),
                    NetPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    IsTestPlan = table.Column<bool>(type: "boolean", nullable: false),
                    ProductsToScrap = table.Column<int>(type: "integer", nullable: true),
                    ProductsToScrapAllegro = table.Column<int>(type: "integer", nullable: true),
                    DaysPerInvoice = table.Column<int>(type: "integer", nullable: false),
                    Ceneo = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleShopping = table.Column<bool>(type: "boolean", nullable: false),
                    Allegro = table.Column<bool>(type: "boolean", nullable: false),
                    Info = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.PlanId);
                });

            migrationBuilder.CreateTable(
                name: "ProductMaps",
                columns: table => new
                {
                    ProductMapId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    Ean = table.Column<string>(type: "text", nullable: true),
                    MainUrl = table.Column<string>(type: "text", nullable: true),
                    ExportedName = table.Column<string>(type: "text", nullable: true),
                    GoogleEan = table.Column<string>(type: "text", nullable: true),
                    GoogleImage = table.Column<string>(type: "text", nullable: true),
                    GoogleExportedName = table.Column<string>(type: "text", nullable: true),
                    GoogleXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    GoogleDeliveryXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CeneoXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CeneoDeliveryXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    GoogleExportedProducer = table.Column<string>(type: "text", nullable: true),
                    CeneoExportedProducer = table.Column<string>(type: "text", nullable: true),
                    GoogleExportedProducerCode = table.Column<string>(type: "text", nullable: true),
                    CeneoExportedProducerCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMaps", x => x.ProductMapId);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    RegionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    CurrencyValue = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.RegionId);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    SettingsId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VerificationRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Semophore = table.Column<int>(type: "integer", nullable: false),
                    SemophoreGoogle = table.Column<int>(type: "integer", nullable: false),
                    WarmUpTime = table.Column<int>(type: "integer", nullable: false),
                    HeadLess = table.Column<bool>(type: "boolean", nullable: false),
                    JavaScript = table.Column<bool>(type: "boolean", nullable: false),
                    Styles = table.Column<bool>(type: "boolean", nullable: false),
                    GetCeneoName = table.Column<bool>(type: "boolean", nullable: false),
                    ControlXY = table.Column<bool>(type: "boolean", nullable: false),
                    ExpandAndCompareGoogleOffers = table.Column<bool>(type: "boolean", nullable: false),
                    HeadLessForGoogleGenerators = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleGeneratorsCount = table.Column<int>(type: "integer", nullable: false),
                    HeadLessForAllegroGenerators = table.Column<bool>(type: "boolean", nullable: false),
                    GeneratorsAllegroCount = table.Column<int>(type: "integer", nullable: false),
                    SemophoreAllegroCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.SettingsId);
                });

            migrationBuilder.CreateTable(
                name: "TaskExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    OperationName = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateVerification",
                columns: table => new
                {
                    AffiliateVerificationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateVerification", x => x.AffiliateVerificationId);
                    table.ForeignKey(
                        name: "FK_AffiliateVerification_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClientProfiles",
                columns: table => new
                {
                    ClientProfileId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CeneoProfileUrl = table.Column<string>(type: "text", nullable: true),
                    CeneoProfileName = table.Column<string>(type: "text", nullable: false),
                    CeneoProfileEmail = table.Column<string>(type: "text", nullable: false),
                    CeneoProfileTelephone = table.Column<string>(type: "text", nullable: true),
                    CeneoProfileProductCount = table.Column<int>(type: "integer", nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScheduledMeetingDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EmailSentCount = table.Column<int>(type: "integer", nullable: false),
                    LastEmailSentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientProfiles", x => x.ClientProfileId);
                    table.ForeignKey(
                        name: "FK_ClientProfiles_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMessages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoOfrPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoOfrClassId = table.Column<int>(type: "integer", nullable: false),
                    StoreName = table.Column<string>(type: "text", nullable: true),
                    Price = table.Column<decimal>(type: "numeric", nullable: true),
                    IsBidding = table.Column<string>(type: "text", nullable: true),
                    Position = table.Column<string>(type: "text", nullable: true),
                    ShippingCostNum = table.Column<decimal>(type: "numeric", nullable: true),
                    CeneoInStock = table.Column<bool>(type: "boolean", nullable: true),
                    ExportedName = table.Column<string>(type: "text", nullable: true),
                    GoogleStoreName = table.Column<string>(type: "text", nullable: true),
                    GooglePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    GooglePosition = table.Column<string>(type: "text", nullable: true),
                    GooglePriceWithDelivery = table.Column<decimal>(type: "numeric", nullable: true),
                    GoogleInStock = table.Column<bool>(type: "boolean", nullable: true),
                    GoogleOfferPerStoreCount = table.Column<int>(type: "integer", nullable: true),
                    GoogleOfferUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoOfrPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoOfrPriceHistories_CoOfrs_CoOfrClassId",
                        column: x => x.CoOfrClassId,
                        principalTable: "CoOfrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoOfrStoreDatas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoOfrClassId = table.Column<int>(type: "integer", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    ProductExternalId = table.Column<string>(type: "text", nullable: true),
                    ExtendedDataApiPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    IsApiProcessed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoOfrStoreDatas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoOfrStoreDatas_CoOfrs_CoOfrClassId",
                        column: x => x.CoOfrClassId,
                        principalTable: "CoOfrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SchedulePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MondayId = table.Column<int>(type: "integer", nullable: true),
                    TuesdayId = table.Column<int>(type: "integer", nullable: true),
                    WednesdayId = table.Column<int>(type: "integer", nullable: true),
                    ThursdayId = table.Column<int>(type: "integer", nullable: true),
                    FridayId = table.Column<int>(type: "integer", nullable: true),
                    SaturdayId = table.Column<int>(type: "integer", nullable: true),
                    SundayId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_FridayId",
                        column: x => x.FridayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_MondayId",
                        column: x => x.MondayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_SaturdayId",
                        column: x => x.SaturdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_SundayId",
                        column: x => x.SundayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_ThursdayId",
                        column: x => x.ThursdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_TuesdayId",
                        column: x => x.TuesdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchedulePlans_DayDetails_WednesdayId",
                        column: x => x.WednesdayId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionName = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    UrlEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CeneoEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BaseEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ApiBotEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UrlScalAleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AleCrawEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AleApiBotEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AleBaseEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MarketPlaceAutomationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PriceComparisonAutomationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DayDetailId = table.Column<int>(type: "integer", nullable: false),
                    LastRunDateOfTask = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleTasks_DayDetails_DayDetailId",
                        column: x => x.DayDetailId,
                        principalTable: "DayDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreName = table.Column<string>(type: "text", nullable: false),
                    StoreProfile = table.Column<string>(type: "text", nullable: true),
                    StoreApiUrl = table.Column<string>(type: "text", nullable: true),
                    StoreApiKey = table.Column<string>(type: "text", nullable: true),
                    StoreLogoUrl = table.Column<string>(type: "text", nullable: true),
                    ProductMapXmlUrl = table.Column<string>(type: "text", nullable: true),
                    ProductMapXmlUrlGoogle = table.Column<string>(type: "text", nullable: true),
                    ProductsToScrap = table.Column<int>(type: "integer", nullable: true),
                    ProductsToScrapAllegro = table.Column<int>(type: "integer", nullable: true),
                    PlanId = table.Column<int>(type: "integer", nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    RemainingDays = table.Column<int>(type: "integer", nullable: false),
                    IsPayingCustomer = table.Column<bool>(type: "boolean", nullable: false),
                    SubscriptionStartDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UserWantsExit = table.Column<bool>(type: "boolean", nullable: false),
                    StoreNameGoogle = table.Column<string>(type: "text", nullable: true),
                    StoreNameCeneo = table.Column<string>(type: "text", nullable: true),
                    UseGoogleXMLFeedPrice = table.Column<bool>(type: "boolean", nullable: false),
                    UseCeneoXMLFeedPrice = table.Column<bool>(type: "boolean", nullable: false),
                    FetchExtendedData = table.Column<bool>(type: "boolean", nullable: false),
                    StoreSystemType = table.Column<int>(type: "integer", nullable: false),
                    IsStorePriceBridgeActive = table.Column<bool>(type: "boolean", nullable: false),
                    StoreNameAllegro = table.Column<string>(type: "text", nullable: true),
                    OnCeneo = table.Column<bool>(type: "boolean", nullable: false),
                    OnGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    OnAllegro = table.Column<bool>(type: "boolean", nullable: false),
                    IsProducer = table.Column<bool>(type: "boolean", nullable: false),
                    FetchExtendedAllegroData = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroRefreshToken = table.Column<string>(type: "text", nullable: true),
                    AllegroApiToken = table.Column<string>(type: "text", nullable: true),
                    AllegroTokenExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsAllegroTokenActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsAllegroPriceBridgeActive = table.Column<bool>(type: "boolean", nullable: false),
                    ImojePaymentProfileId = table.Column<string>(type: "text", nullable: true),
                    IsRecurringActive = table.Column<bool>(type: "boolean", nullable: false),
                    CardMaskedNumber = table.Column<string>(type: "text", nullable: true),
                    CardBrand = table.Column<string>(type: "text", nullable: true),
                    CardExpYear = table.Column<string>(type: "text", nullable: true),
                    CardExpMonth = table.Column<string>(type: "text", nullable: true),
                    UseGPID = table.Column<bool>(type: "boolean", nullable: false),
                    UseWRGA = table.Column<bool>(type: "boolean", nullable: false),
                    UseAdditionalCatalogsForGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    CollectGoogleStoreLinks = table.Column<bool>(type: "boolean", nullable: false),
                    IsApiExportEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ApiExportToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.StoreId);
                    table.ForeignKey(
                        name: "FK_Stores_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "PlanId");
                });

            migrationBuilder.CreateTable(
                name: "GoogleScrapingProducts",
                columns: table => new
                {
                    ScrapingProductId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    GoogleUrl = table.Column<string>(type: "text", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    IsScraped = table.Column<bool>(type: "boolean", nullable: true),
                    OffersCount = table.Column<int>(type: "integer", nullable: false),
                    PriceSafariRaportId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleScrapingProducts", x => x.ScrapingProductId);
                    table.ForeignKey(
                        name: "FK_GoogleScrapingProducts_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "RegionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClientProfileContactLabel",
                columns: table => new
                {
                    ClientProfilesClientProfileId = table.Column<int>(type: "integer", nullable: false),
                    LabelsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientProfileContactLabel", x => new { x.ClientProfilesClientProfileId, x.LabelsId });
                    table.ForeignKey(
                        name: "FK_ClientProfileContactLabel_ClientProfiles_ClientProfilesClie~",
                        column: x => x.ClientProfilesClientProfileId,
                        principalTable: "ClientProfiles",
                        principalColumn: "ClientProfileId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientProfileContactLabel_ContactLabels_LabelsId",
                        column: x => x.LabelsId,
                        principalTable: "ContactLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroOffersToScrape",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllegroOfferUrl = table.Column<string>(type: "text", nullable: false),
                    AllegroOfferId = table.Column<long>(type: "bigint", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    AllegroProductIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    IsScraped = table.Column<bool>(type: "boolean", nullable: false),
                    IsRejected = table.Column<bool>(type: "boolean", nullable: false),
                    AddedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CollectedPricesCount = table.Column<int>(type: "integer", nullable: false),
                    IsProcessing = table.Column<bool>(type: "boolean", nullable: false),
                    IsApiProcessed = table.Column<bool>(type: "boolean", nullable: true),
                    ApiAllegroPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ApiAllegroPriceFromUser = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ApiAllegroCommission = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    AnyPromoActive = table.Column<bool>(type: "boolean", nullable: true),
                    IsSubsidyActive = table.Column<bool>(type: "boolean", nullable: true),
                    AllegroEan = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroOffersToScrape", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroOffersToScrape_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroProducts",
                columns: table => new
                {
                    AllegroProductId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    AllegroProductName = table.Column<string>(type: "text", nullable: false),
                    AllegroOfferUrl = table.Column<string>(type: "text", nullable: false),
                    IdOnAllegro = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AllegroSku = table.Column<string>(type: "text", nullable: true),
                    AddedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AllegroMarginPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    AllegroMarginPriceUpdatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AllegroEan = table.Column<string>(type: "text", nullable: true),
                    IsScrapable = table.Column<bool>(type: "boolean", nullable: false),
                    IsRejected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroProducts", x => x.AllegroProductId);
                    table.ForeignKey(
                        name: "FK_AllegroProducts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroScrapeHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    ProcessedUrlsCount = table.Column<int>(type: "integer", nullable: false),
                    SavedOffersCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroScrapeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroScrapeHistories_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    CategoryName = table.Column<string>(type: "text", nullable: false),
                    CategoryUrl = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                    table.ForeignKey(
                        name: "FK_Categories_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitorPresets",
                columns: table => new
                {
                    PresetId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PresetName = table.Column<string>(type: "text", nullable: false),
                    SourceGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    SourceCeneo = table.Column<bool>(type: "boolean", nullable: false),
                    UseUnmarkedStores = table.Column<bool>(type: "boolean", nullable: false),
                    MinDeliveryDays = table.Column<int>(type: "integer", nullable: false),
                    MaxDeliveryDays = table.Column<int>(type: "integer", nullable: false),
                    IncludeNoDeliveryInfo = table.Column<bool>(type: "boolean", nullable: false),
                    NowInUse = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitorPresets", x => x.PresetId);
                    table.ForeignKey(
                        name: "FK_CompetitorPresets_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Flags",
                columns: table => new
                {
                    FlagId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FlagName = table.Column<string>(type: "text", nullable: false),
                    FlagColor = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    IsMarketplace = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flags", x => x.FlagId);
                    table.ForeignKey(
                        name: "FK_Flags_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    InvoiceId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: true),
                    ArchivedStoreName = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DaysIncluded = table.Column<int>(type: "integer", nullable: false),
                    UrlsIncluded = table.Column<int>(type: "integer", nullable: true),
                    UrlsIncludedAllegro = table.Column<int>(type: "integer", nullable: true),
                    AppliedDiscountPercentage = table.Column<decimal>(type: "numeric", nullable: false),
                    AppliedDiscountAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    OriginalProformaNumber = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsPaidByCard = table.Column<bool>(type: "boolean", nullable: false),
                    IsSentByEmail = table.Column<bool>(type: "boolean", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    PostalCode = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    NIP = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.InvoiceId);
                    table.ForeignKey(
                        name: "FK_Invoices_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "PlanId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PriceSafariReports",
                columns: table => new
                {
                    ReportId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportName = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReadyDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ProductIds = table.Column<List<int>>(type: "integer[]", nullable: true),
                    RegionIds = table.Column<List<int>>(type: "integer[]", nullable: false),
                    Prepared = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSafariReports", x => x.ReportId);
                    table.ForeignKey(
                        name: "FK_PriceSafariReports_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceValues",
                columns: table => new
                {
                    PriceValueClassId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    SetPrice1 = table.Column<decimal>(type: "numeric", nullable: false),
                    SetPrice2 = table.Column<decimal>(type: "numeric", nullable: false),
                    UsePriceDiff = table.Column<bool>(type: "boolean", nullable: false),
                    PriceStep = table.Column<decimal>(type: "numeric", nullable: false),
                    SetSafariPrice1 = table.Column<decimal>(type: "numeric", nullable: false),
                    SetSafariPrice2 = table.Column<decimal>(type: "numeric", nullable: false),
                    UsePriceDiffSafari = table.Column<bool>(type: "boolean", nullable: false),
                    PriceIndexTargetPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    UsePriceWithDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    IdentifierForSimulation = table.Column<string>(type: "text", nullable: false),
                    UseMarginForSimulation = table.Column<bool>(type: "boolean", nullable: false),
                    EnforceMinimalMargin = table.Column<bool>(type: "boolean", nullable: false),
                    MinimalMarginPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AllegroUsePriceDiff = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroPriceStep = table.Column<decimal>(type: "numeric", nullable: false),
                    AllegroSetPrice1 = table.Column<decimal>(type: "numeric", nullable: false),
                    AllegroSetPrice2 = table.Column<decimal>(type: "numeric", nullable: false),
                    AllegroPriceIndexTargetPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AllegroIdentifierForSimulation = table.Column<string>(type: "text", nullable: false),
                    AllegroUseMarginForSimulation = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroEnforceMinimalMargin = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroMinimalMarginPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AllegroIncludeCommisionInPriceChange = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroChangePriceForBagdeSuperPrice = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroChangePriceForBagdeTopOffer = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroChangePriceForBagdeBestPriceGuarantee = table.Column<bool>(type: "boolean", nullable: false),
                    AllegroChangePriceForBagdeInCampaign = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceValues", x => x.PriceValueClassId);
                    table.ForeignKey(
                        name: "FK_PriceValues_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleTaskStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduleTaskId = table.Column<int>(type: "integer", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleTaskStores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleTaskStores_ScheduleTasks_ScheduleTaskId",
                        column: x => x.ScheduleTaskId,
                        principalTable: "ScheduleTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleTaskStores_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScrapHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ProductCount = table.Column<int>(type: "integer", nullable: false),
                    PriceCount = table.Column<int>(type: "integer", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScrapHistories_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPaymentDatas",
                columns: table => new
                {
                    PaymentDataId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    PostalCode = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    NIP = table.Column<string>(type: "text", nullable: true),
                    InvoiceAutoMail = table.Column<string>(type: "text", nullable: true),
                    InvoiceAutoMailSend = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPaymentDatas", x => x.PaymentDataId);
                    table.ForeignKey(
                        name: "FK_UserPaymentDatas_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserStores",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStores", x => new { x.UserId, x.StoreId });
                    table.ForeignKey(
                        name: "FK_UserStores_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserStores_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceData",
                columns: table => new
                {
                    PriceDataId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScrapingProductId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceWithDelivery = table.Column<decimal>(type: "numeric", nullable: false),
                    StoreName = table.Column<string>(type: "text", nullable: false),
                    OfferUrl = table.Column<string>(type: "text", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceData", x => x.PriceDataId);
                    table.ForeignKey(
                        name: "FK_PriceData_GoogleScrapingProducts_ScrapingProductId",
                        column: x => x.ScrapingProductId,
                        principalTable: "GoogleScrapingProducts",
                        principalColumn: "ScrapingProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceData_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "RegionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Producer = table.Column<string>(type: "text", nullable: true),
                    AddedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    OfferUrl = table.Column<string>(type: "text", nullable: true),
                    FoundOnCeneoDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FoundOnGoogle = table.Column<bool>(type: "boolean", nullable: true),
                    FoundOnGoogleDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OnGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    GoogleUrl = table.Column<string>(type: "text", nullable: true),
                    GoogleGid = table.Column<string>(type: "text", nullable: true),
                    ProductNameInStoreForGoogle = table.Column<string>(type: "text", nullable: true),
                    EanGoogle = table.Column<string>(type: "text", nullable: true),
                    ImgUrlGoogle = table.Column<string>(type: "text", nullable: true),
                    ExternalId = table.Column<int>(type: "integer", nullable: true),
                    CatalogNumber = table.Column<string>(type: "text", nullable: true),
                    Ean = table.Column<string>(type: "text", nullable: true),
                    MainUrl = table.Column<string>(type: "text", nullable: true),
                    IsScrapable = table.Column<bool>(type: "boolean", nullable: false),
                    IsRejected = table.Column<bool>(type: "boolean", nullable: false),
                    ExportedNameCeneo = table.Column<string>(type: "text", nullable: true),
                    MarginPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    MarginPriceUpdatedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    GoogleXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    GoogleDeliveryXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CeneoXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CeneoDeliveryXMLPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ProducerCode = table.Column<string>(type: "text", nullable: true),
                    GoogleScrapingProductScrapingProductId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_Products_GoogleScrapingProducts_GoogleScrapingProductScrapi~",
                        column: x => x.GoogleScrapingProductScrapingProductId,
                        principalTable: "GoogleScrapingProducts",
                        principalColumn: "ScrapingProductId");
                    table.ForeignKey(
                        name: "FK_Products_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroScrapedOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SellerName = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DeliveryCost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DeliveryTime = table.Column<int>(type: "integer", nullable: true),
                    Popularity = table.Column<int>(type: "integer", nullable: true),
                    SuperSeller = table.Column<bool>(type: "boolean", nullable: false),
                    Smart = table.Column<bool>(type: "boolean", nullable: false),
                    IsBestPriceGuarantee = table.Column<bool>(type: "boolean", nullable: false),
                    TopOffer = table.Column<bool>(type: "boolean", nullable: false),
                    SuperPrice = table.Column<bool>(type: "boolean", nullable: false),
                    Promoted = table.Column<bool>(type: "boolean", nullable: false),
                    Sponsored = table.Column<bool>(type: "boolean", nullable: false),
                    IdAllegro = table.Column<long>(type: "bigint", nullable: false),
                    AllegroOfferToScrapeId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroScrapedOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroScrapedOffers_AllegroOffersToScrape_AllegroOfferToSc~",
                        column: x => x.AllegroOfferToScrapeId,
                        principalTable: "AllegroOffersToScrape",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllegroProductId = table.Column<int>(type: "integer", nullable: false),
                    AllegroScrapeHistoryId = table.Column<int>(type: "integer", nullable: false),
                    SellerName = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DeliveryCost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DeliveryTime = table.Column<int>(type: "integer", nullable: true),
                    Popularity = table.Column<int>(type: "integer", nullable: true),
                    SuperSeller = table.Column<bool>(type: "boolean", nullable: false),
                    Smart = table.Column<bool>(type: "boolean", nullable: false),
                    IsBestPriceGuarantee = table.Column<bool>(type: "boolean", nullable: false),
                    TopOffer = table.Column<bool>(type: "boolean", nullable: false),
                    SuperPrice = table.Column<bool>(type: "boolean", nullable: false),
                    Promoted = table.Column<bool>(type: "boolean", nullable: false),
                    Sponsored = table.Column<bool>(type: "boolean", nullable: false),
                    IdAllegro = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistories_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistories_AllegroScrapeHistories_AllegroScrapeH~",
                        column: x => x.AllegroScrapeHistoryId,
                        principalTable: "AllegroScrapeHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroPriceHistoryExtendedInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllegroProductId = table.Column<int>(type: "integer", nullable: false),
                    ScrapHistoryId = table.Column<int>(type: "integer", nullable: false),
                    ApiAllegroPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ApiAllegroPriceFromUser = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ApiAllegroCommission = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    AnyPromoActive = table.Column<bool>(type: "boolean", nullable: true),
                    IsSubsidyActive = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceHistoryExtendedInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistoryExtendedInfos_AllegroProducts_AllegroPro~",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId");
                    table.ForeignKey(
                        name: "FK_AllegroPriceHistoryExtendedInfos_AllegroScrapeHistories_Scr~",
                        column: x => x.ScrapHistoryId,
                        principalTable: "AllegroScrapeHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StrategyMode = table.Column<int>(type: "integer", nullable: false),
                    CompetitorPresetId = table.Column<int>(type: "integer", nullable: true),
                    PriceStep = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsPriceStepPercent = table.Column<bool>(type: "boolean", nullable: false),
                    PriceIndexTargetPercent = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    UsePurchasePrice = table.Column<bool>(type: "boolean", nullable: false),
                    EnforceMinimalMarkup = table.Column<bool>(type: "boolean", nullable: false),
                    MinimalMarkupValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsMinimalMarkupPercent = table.Column<bool>(type: "boolean", nullable: false),
                    EnforceMaxMarkup = table.Column<bool>(type: "boolean", nullable: false),
                    MaxMarkupValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsMaxMarkupPercent = table.Column<bool>(type: "boolean", nullable: false),
                    SkipIfMarkupLimited = table.Column<bool>(type: "boolean", nullable: false),
                    UsePriceWithDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    RequireOwnOfferOnCeneo = table.Column<bool>(type: "boolean", nullable: false),
                    RequireOwnOfferOnGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    MarketplaceIncludeCommission = table.Column<bool>(type: "boolean", nullable: false),
                    MarketplaceChangePriceForBadgeSuperPrice = table.Column<bool>(type: "boolean", nullable: false),
                    MarketplaceChangePriceForBadgeTopOffer = table.Column<bool>(type: "boolean", nullable: false),
                    MarketplaceChangePriceForBadgeBestPriceGuarantee = table.Column<bool>(type: "boolean", nullable: false),
                    MarketplaceChangePriceForBadgeInCampaign = table.Column<bool>(type: "boolean", nullable: false),
                    BlockAtSmartValue = table.Column<bool>(type: "boolean", nullable: false),
                    SkipIfValueGoBelow = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    StoreClassStoreId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationRules_CompetitorPresets_CompetitorPresetId",
                        column: x => x.CompetitorPresetId,
                        principalTable: "CompetitorPresets",
                        principalColumn: "PresetId");
                    table.ForeignKey(
                        name: "FK_AutomationRules_Stores_StoreClassStoreId",
                        column: x => x.StoreClassStoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId");
                    table.ForeignKey(
                        name: "FK_AutomationRules_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitorPresetItems",
                columns: table => new
                {
                    CompetitorPresetItemId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PresetId = table.Column<int>(type: "integer", nullable: false),
                    StoreName = table.Column<string>(type: "text", nullable: false),
                    DataSource = table.Column<int>(type: "integer", nullable: false),
                    UseCompetitor = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitorPresetItems", x => x.CompetitorPresetItemId);
                    table.ForeignKey(
                        name: "FK_CompetitorPresetItems_CompetitorPresets_PresetId",
                        column: x => x.PresetId,
                        principalTable: "CompetitorPresets",
                        principalColumn: "PresetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlobalPriceReports",
                columns: table => new
                {
                    ReportId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScrapingProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    CalculatedPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceWithDelivery = table.Column<decimal>(type: "numeric", nullable: false),
                    CalculatedPriceWithDelivery = table.Column<decimal>(type: "numeric", nullable: false),
                    StoreName = table.Column<string>(type: "text", nullable: false),
                    OfferUrl = table.Column<string>(type: "text", nullable: true),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    PriceSafariReportId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalPriceReports", x => x.ReportId);
                    table.ForeignKey(
                        name: "FK_GlobalPriceReports_PriceSafariReports_PriceSafariReportId",
                        column: x => x.PriceSafariReportId,
                        principalTable: "PriceSafariReports",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GlobalPriceReports_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GlobalPriceReports_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "RegionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    StoreName = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    IsBidding = table.Column<string>(type: "text", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: true),
                    ScrapHistoryId = table.Column<int>(type: "integer", nullable: false),
                    ShippingCostNum = table.Column<decimal>(type: "numeric", nullable: true),
                    CeneoInStock = table.Column<bool>(type: "boolean", nullable: true),
                    IsGoogle = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleInStock = table.Column<bool>(type: "boolean", nullable: true),
                    GoogleOfferPerStoreCount = table.Column<int>(type: "integer", nullable: true),
                    GoogleOfferUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceHistories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceHistories_ScrapHistories_ScrapHistoryId",
                        column: x => x.ScrapHistoryId,
                        principalTable: "ScrapHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceHistoryExtendedInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ScrapHistoryId = table.Column<int>(type: "integer", nullable: false),
                    CeneoSalesCount = table.Column<int>(type: "integer", nullable: true),
                    ExtendedDataApiPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceHistoryExtendedInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceHistoryExtendedInfos_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceHistoryExtendedInfos_ScrapHistories_ScrapHistoryId",
                        column: x => x.ScrapHistoryId,
                        principalTable: "ScrapHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductFlags",
                columns: table => new
                {
                    ProductFlagId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FlagId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    AllegroProductId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFlags", x => x.ProductFlagId);
                    table.ForeignKey(
                        name: "FK_ProductFlags_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId");
                    table.ForeignKey(
                        name: "FK_ProductFlags_Flags_FlagId",
                        column: x => x.FlagId,
                        principalTable: "Flags",
                        principalColumn: "FlagId");
                    table.ForeignKey(
                        name: "FK_ProductFlags_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductGoogleCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    GoogleCid = table.Column<string>(type: "text", nullable: true),
                    GoogleGid = table.Column<string>(type: "text", nullable: true),
                    GoogleHid = table.Column<string>(type: "text", nullable: true),
                    IsExtendedOfferByHid = table.Column<bool>(type: "boolean", nullable: false),
                    FoundDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductGoogleCatalogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductGoogleCatalogs_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllegroPriceBridgeBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutionDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    IsAutomation = table.Column<bool>(type: "boolean", nullable: false),
                    AutomationRuleId = table.Column<int>(type: "integer", nullable: true),
                    AllegroScrapeHistoryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    SuccessfulCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalProductsCount = table.Column<int>(type: "integer", nullable: true),
                    TargetMetCount = table.Column<int>(type: "integer", nullable: true),
                    TargetUnmetCount = table.Column<int>(type: "integer", nullable: true),
                    PriceIncreasedCount = table.Column<int>(type: "integer", nullable: true),
                    PriceDecreasedCount = table.Column<int>(type: "integer", nullable: true),
                    PriceMaintainedCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceBridgeBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeBatches_AllegroScrapeHistories_AllegroScr~",
                        column: x => x.AllegroScrapeHistoryId,
                        principalTable: "AllegroScrapeHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeBatches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeBatches_AutomationRules_AutomationRuleId",
                        column: x => x.AutomationRuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeBatches_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationProductAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AutomationRuleId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    AllegroProductId = table.Column<int>(type: "integer", nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationProductAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationProductAssignments_AllegroProducts_AllegroProduct~",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId");
                    table.ForeignKey(
                        name: "FK_AutomationProductAssignments_AutomationRules_AutomationRule~",
                        column: x => x.AutomationRuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AutomationProductAssignments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId");
                });

            migrationBuilder.CreateTable(
                name: "PriceBridgeBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsAutomation = table.Column<bool>(type: "boolean", nullable: false),
                    AutomationRuleId = table.Column<int>(type: "integer", nullable: true),
                    StoreId = table.Column<int>(type: "integer", nullable: false),
                    ScrapHistoryId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    ExecutionDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SuccessfulCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalProductsCount = table.Column<int>(type: "integer", nullable: true),
                    TargetMetCount = table.Column<int>(type: "integer", nullable: true),
                    TargetUnmetCount = table.Column<int>(type: "integer", nullable: true),
                    PriceIncreasedCount = table.Column<int>(type: "integer", nullable: true),
                    PriceDecreasedCount = table.Column<int>(type: "integer", nullable: true),
                    PriceMaintainedCount = table.Column<int>(type: "integer", nullable: true),
                    ExportMethod = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceBridgeBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceBridgeBatches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PriceBridgeBatches_AutomationRules_AutomationRuleId",
                        column: x => x.AutomationRuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PriceBridgeBatches_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "StoreId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllegroPriceBridgeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllegroPriceBridgeBatchId = table.Column<int>(type: "integer", nullable: false),
                    AllegroProductId = table.Column<int>(type: "integer", nullable: false),
                    AllegroOfferId = table.Column<string>(type: "text", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: false),
                    MarginPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    IncludeCommissionInMargin = table.Column<bool>(type: "boolean", nullable: false),
                    PriceBefore = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CommissionBefore = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    RankingBefore = table.Column<string>(type: "text", nullable: false),
                    PriceAfter_Simulated = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RankingAfter_Simulated = table.Column<string>(type: "text", nullable: false),
                    PriceAfter_Verified = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CommissionAfter_Verified = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Mode = table.Column<string>(type: "text", nullable: true),
                    PriceIndexTarget = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    StepPriceApplied = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MinPriceLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MaxPriceLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    WasLimitedByMin = table.Column<bool>(type: "boolean", nullable: true),
                    WasLimitedByMax = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllegroPriceBridgeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeItems_AllegroPriceBridgeBatches_AllegroPr~",
                        column: x => x.AllegroPriceBridgeBatchId,
                        principalTable: "AllegroPriceBridgeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AllegroPriceBridgeItems_AllegroProducts_AllegroProductId",
                        column: x => x.AllegroProductId,
                        principalTable: "AllegroProducts",
                        principalColumn: "AllegroProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceBridgeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PriceBridgeBatchId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    PriceBefore = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PriceAfter = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MarginPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    RankingGoogleBefore = table.Column<string>(type: "text", nullable: true),
                    RankingCeneoBefore = table.Column<string>(type: "text", nullable: true),
                    RankingGoogleAfterSimulated = table.Column<string>(type: "text", nullable: true),
                    RankingCeneoAfterSimulated = table.Column<string>(type: "text", nullable: true),
                    Mode = table.Column<string>(type: "text", nullable: true),
                    PriceIndexTarget = table.Column<decimal>(type: "numeric", nullable: true),
                    StepPriceApplied = table.Column<decimal>(type: "numeric", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    MinPriceLimit = table.Column<decimal>(type: "numeric", nullable: true),
                    MaxPriceLimit = table.Column<decimal>(type: "numeric", nullable: true),
                    WasLimitedByMin = table.Column<bool>(type: "boolean", nullable: true),
                    WasLimitedByMax = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceBridgeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceBridgeItems_PriceBridgeBatches_PriceBridgeBatchId",
                        column: x => x.PriceBridgeBatchId,
                        principalTable: "PriceBridgeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceBridgeItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateVerification_UserId",
                table: "AffiliateVerification",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AllegroOffersToScrape_StoreId",
                table: "AllegroOffersToScrape",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_AllegroScrapeHistoryId",
                table: "AllegroPriceBridgeBatches",
                column: "AllegroScrapeHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_AutomationRuleId",
                table: "AllegroPriceBridgeBatches",
                column: "AutomationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_StoreId",
                table: "AllegroPriceBridgeBatches",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeBatches_UserId",
                table: "AllegroPriceBridgeBatches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeItems_AllegroPriceBridgeBatchId",
                table: "AllegroPriceBridgeItems",
                column: "AllegroPriceBridgeBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceBridgeItems_AllegroProductId",
                table: "AllegroPriceBridgeItems",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistories_AllegroProductId",
                table: "AllegroPriceHistories",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistories_AllegroScrapeHistoryId",
                table: "AllegroPriceHistories",
                column: "AllegroScrapeHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistoryExtendedInfos_AllegroProductId",
                table: "AllegroPriceHistoryExtendedInfos",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroPriceHistoryExtendedInfos_ScrapHistoryId",
                table: "AllegroPriceHistoryExtendedInfos",
                column: "ScrapHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroProducts_StoreId",
                table: "AllegroProducts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroScrapedOffers_AllegroOfferToScrapeId",
                table: "AllegroScrapedOffers",
                column: "AllegroOfferToScrapeId");

            migrationBuilder.CreateIndex(
                name: "IX_AllegroScrapeHistories_StoreId",
                table: "AllegroScrapeHistories",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProductAssignments_AllegroProductId",
                table: "AutomationProductAssignments",
                column: "AllegroProductId",
                unique: true,
                filter: "\"AllegroProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProductAssignments_AutomationRuleId",
                table: "AutomationProductAssignments",
                column: "AutomationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProductAssignments_ProductId",
                table: "AutomationProductAssignments",
                column: "ProductId",
                unique: true,
                filter: "\"ProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_CompetitorPresetId",
                table: "AutomationRules",
                column: "CompetitorPresetId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_StoreClassStoreId",
                table: "AutomationRules",
                column: "StoreClassStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_StoreId",
                table: "AutomationRules",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_StoreId",
                table: "Categories",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientProfileContactLabel_LabelsId",
                table: "ClientProfileContactLabel",
                column: "LabelsId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientProfiles_CreatedByUserId",
                table: "ClientProfiles",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitorPresetItems_PresetId",
                table: "CompetitorPresetItems",
                column: "PresetId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitorPresets_StoreId",
                table: "CompetitorPresets",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_CoOfrPriceHistories_CoOfrClassId",
                table: "CoOfrPriceHistories",
                column: "CoOfrClassId");

            migrationBuilder.CreateIndex(
                name: "IX_CoOfrStoreDatas_CoOfrClassId",
                table: "CoOfrStoreDatas",
                column: "CoOfrClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Flags_StoreId",
                table: "Flags",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalPriceReports_PriceSafariReportId",
                table: "GlobalPriceReports",
                column: "PriceSafariReportId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalPriceReports_ProductId",
                table: "GlobalPriceReports",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalPriceReports_RegionId",
                table: "GlobalPriceReports",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_GoogleScrapingProducts_RegionId",
                table: "GoogleScrapingProducts",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PlanId",
                table: "Invoices",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StoreId",
                table: "Invoices",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeBatches_AutomationRuleId",
                table: "PriceBridgeBatches",
                column: "AutomationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeBatches_StoreId",
                table: "PriceBridgeBatches",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeBatches_UserId",
                table: "PriceBridgeBatches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeItems_PriceBridgeBatchId",
                table: "PriceBridgeItems",
                column: "PriceBridgeBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceBridgeItems_ProductId",
                table: "PriceBridgeItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceData_RegionId",
                table: "PriceData",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceData_ScrapingProductId",
                table: "PriceData",
                column: "ScrapingProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_ProductId",
                table: "PriceHistories",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistories_ScrapHistoryId",
                table: "PriceHistories",
                column: "ScrapHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistoryExtendedInfos_ProductId_ScrapHistoryId",
                table: "PriceHistoryExtendedInfos",
                columns: new[] { "ProductId", "ScrapHistoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceHistoryExtendedInfos_ScrapHistoryId",
                table: "PriceHistoryExtendedInfos",
                column: "ScrapHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceSafariReports_StoreId",
                table: "PriceSafariReports",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceValues_StoreId",
                table: "PriceValues",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_AllegroProductId",
                table: "ProductFlags",
                column: "AllegroProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_FlagId",
                table: "ProductFlags",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFlags_ProductId",
                table: "ProductFlags",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductGoogleCatalogs_ProductId",
                table: "ProductGoogleCatalogs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_GoogleScrapingProductScrapingProductId",
                table: "Products",
                column: "GoogleScrapingProductScrapingProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId",
                table: "Products",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_FridayId",
                table: "SchedulePlans",
                column: "FridayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_MondayId",
                table: "SchedulePlans",
                column: "MondayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_SaturdayId",
                table: "SchedulePlans",
                column: "SaturdayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_SundayId",
                table: "SchedulePlans",
                column: "SundayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_ThursdayId",
                table: "SchedulePlans",
                column: "ThursdayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_TuesdayId",
                table: "SchedulePlans",
                column: "TuesdayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePlans_WednesdayId",
                table: "SchedulePlans",
                column: "WednesdayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTasks_DayDetailId",
                table: "ScheduleTasks",
                column: "DayDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTaskStores_ScheduleTaskId",
                table: "ScheduleTaskStores",
                column: "ScheduleTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTaskStores_StoreId",
                table: "ScheduleTaskStores",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapHistories_StoreId",
                table: "ScrapHistories",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_PlanId",
                table: "Stores",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMessages_UserId",
                table: "UserMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPaymentDatas_StoreId",
                table: "UserPaymentDatas",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserStores_StoreId",
                table: "UserStores",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliateVerification");

            migrationBuilder.DropTable(
                name: "AllegroPriceBridgeItems");

            migrationBuilder.DropTable(
                name: "AllegroPriceHistories");

            migrationBuilder.DropTable(
                name: "AllegroPriceHistoryExtendedInfos");

            migrationBuilder.DropTable(
                name: "AllegroScrapedOffers");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AutomationProductAssignments");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "CeneoFieldMappings");

            migrationBuilder.DropTable(
                name: "ClientProfileContactLabel");

            migrationBuilder.DropTable(
                name: "CompetitorPresetItems");

            migrationBuilder.DropTable(
                name: "CoOfrPriceHistories");

            migrationBuilder.DropTable(
                name: "CoOfrStoreDatas");

            migrationBuilder.DropTable(
                name: "DeviceStatuses");

            migrationBuilder.DropTable(
                name: "EmailTemplates");

            migrationBuilder.DropTable(
                name: "GlobalPriceReports");

            migrationBuilder.DropTable(
                name: "GoogleFieldMappings");

            migrationBuilder.DropTable(
                name: "InvoiceCounters");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "PriceBridgeItems");

            migrationBuilder.DropTable(
                name: "PriceData");

            migrationBuilder.DropTable(
                name: "PriceHistories");

            migrationBuilder.DropTable(
                name: "PriceHistoryExtendedInfos");

            migrationBuilder.DropTable(
                name: "PriceValues");

            migrationBuilder.DropTable(
                name: "ProductFlags");

            migrationBuilder.DropTable(
                name: "ProductGoogleCatalogs");

            migrationBuilder.DropTable(
                name: "ProductMaps");

            migrationBuilder.DropTable(
                name: "SchedulePlans");

            migrationBuilder.DropTable(
                name: "ScheduleTaskStores");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TaskExecutionLogs");

            migrationBuilder.DropTable(
                name: "UserMessages");

            migrationBuilder.DropTable(
                name: "UserPaymentDatas");

            migrationBuilder.DropTable(
                name: "UserStores");

            migrationBuilder.DropTable(
                name: "AllegroPriceBridgeBatches");

            migrationBuilder.DropTable(
                name: "AllegroOffersToScrape");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "ClientProfiles");

            migrationBuilder.DropTable(
                name: "ContactLabels");

            migrationBuilder.DropTable(
                name: "CoOfrs");

            migrationBuilder.DropTable(
                name: "PriceSafariReports");

            migrationBuilder.DropTable(
                name: "PriceBridgeBatches");

            migrationBuilder.DropTable(
                name: "ScrapHistories");

            migrationBuilder.DropTable(
                name: "AllegroProducts");

            migrationBuilder.DropTable(
                name: "Flags");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "ScheduleTasks");

            migrationBuilder.DropTable(
                name: "AllegroScrapeHistories");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropTable(
                name: "GoogleScrapingProducts");

            migrationBuilder.DropTable(
                name: "DayDetails");

            migrationBuilder.DropTable(
                name: "CompetitorPresets");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
