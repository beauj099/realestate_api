# Property data integration brief — South Africa

Handoff document for the agent/session building the property app. Everything here was probed against the live
services on **26 September 2026**. Field names are stable enough to build against; counts and values move.

Scope: turn a street address into a normalised property record plus comparable sales, plus the assets for a
printable valuation report. Cape Town is fully working with no API key. National coverage needs one trial key.

---

## 1. The pipeline (verified end to end)

Worked example throughout: **17 Pine Road, Claremont, Cape Town**.

### Step 1a — Address → erf, no geocoder (Cape Town)

Cape Town stores the street *type* in its own field, so normalise first: strip `Road/Rd/Avenue/Ave/Street/St`
from the street name, uppercase everything.

```
GET https://services6.arcgis.com/nyYfO9SxHU2ChQd9/arcgis/rest/services/Property/FeatureServer/0/query
  ?where=ADR_NO=17 AND STR_NAME='PINE' AND OFC_SBRB_NAME='CLAREMONT'
  &outFields=PRTY_NMBR,SG26_CODE,ZONING,WARD_NAME,LU_LGL_STS_DSCR
  &returnGeometry=true&outSR=4326&f=json
```

Returns `PRTY_NMBR: "53927"`, `SG26_CODE: "C0160007000539270000000000"`, `ZONING: "General Residential 2"`.

### Step 1b — Address → erf via point-in-polygon (preferred; survives messy input)

Geocode to WGS84 first (see §7 for options), then:

```
GET <same layer>/query
  ?geometry={"x":18.470992,"y":-33.989740,"spatialReference":{"wkid":4326}}
  &geometryType=esriGeometryPoint&inSR=4326&spatialRel=esriSpatialRelIntersects
  &outFields=PRTY_NMBR,SG26_CODE,OFC_SBRB_NAME,ZONING,ADR_NO,STR_NAME,WARD_NAME
  &returnGeometry=false&f=json
```

Verified: that point returns erf 53927, ward 59, Claremont. Build on this path — it is the one that generalises.

### Step 2 — Erf → valuation roll, attributes, comparables

Three plain GET URLs, no auth, ASP.NET pages returning HTML (parse the tables):

```
https://web1.capetown.gov.za/web1/gv2025/Results?Search=ADD,17,PINE
  → valuation reference CCT010812600000 | 53927 CAPE TOWN | RESIDENTIAL
    17 PINE ROAD CLAREMONT | 1085.0000 m² | R 7 100 000 @ 1 Jul 2025 | GV2025
    effective 2026-07-01 | dispute expiry 2026-04-30

https://web1.capetown.gov.za/web1/gv2025/DetStructRes?parcelid_i=cct010812600000&id=<n>&rateCategory=<c>&propertyValue=<v>
  → Erf/Farm Number 53927 | Total Extent 1085 | Dwelling Extent 300 | Deed Extent 1085

https://web1.capetown.gov.za/web1/gv2025/Sales?parcelid=cct010812600000
  → 2 237 rows: Valuation Reference | Physical Address | Registered Description
                | ERF EXTENT | DWELLING EXTENT | Sale Date | Sale Price
```

Search modes on the roll: property reference, erf, sectional title, site address, farm. The results URL is
`?Search=ADD,<number>,<streetname>` for address and `?Search=VAL,<reference>` for reference.

### Step 3 — Parcel polygon → true erf size

Request geometry in `outSR=4326` and compute the geodesic area yourself:

```js
const R = 6378137, rad = Math.PI / 180;
function geodesicArea(ring) {           // ring: [[lng,lat], ...] closed
  let s = 0;
  for (let i = 0; i < ring.length - 1; i++) {
    const [x1, y1] = ring[i], [x2, y2] = ring[i + 1];
    s += (x2 - x1) * rad * (2 + Math.sin(y1 * rad) + Math.sin(y2 * rad));
  }
  return Math.abs(s * R * R / 2);
}
```

Verified: 1 079 m² against the deed's 1 085 m² (0.6% out). **Never use `Shape__Area`** — it is Web Mercator and
reported 1 569 m² for the same erf, ~45% high at Cape Town's latitude.

### Step 4 — Parcel polygon → buildings and heights

```
GET https://esapqa.capetown.gov.za/agsext/rest/services/Theme_Based/ODP_SPLIT_6/FeatureServer/2/query
  ?geometry=<parcel polygon, NOT its envelope>&geometryType=esriGeometryPolygon&inSR=4326
  &spatialRel=esriSpatialRelIntersects&outFields=*&returnGeometry=true&outSR=4326&f=json
```

Fields: `BLD_HGT`, `ACQS_MTHD`, `ACQS_PRD`, `DATA_SRC`, `DMLT_PRD`, `Shape__Area`, `Shape__Length`.

Verified for erf 53927: main building 345 m² roof at 7.6 m, plus outbuildings of 263, 68, 62 and 27 m².

Two traps: an **envelope** query returns the neighbours' buildings (it returned 7 features) — clip with the
parcel polygon; and `ACQS_PRD: 201312` means the capture is from December 2013, so join building plan approvals
for anything built since.

### Step 5 — Join the rest

| Data | Join key | Endpoint |
|---|---|---|
| Zoning detail | `SG26_CODE` | `…/services/Zoning/FeatureServer/0` |
| Building plan approvals | `ERF_Number` | `…/services/Building_Plan_Approvals_2014_to_2025/FeatureServer/0` |
| Suburb medians GV2022→GV2025 | `OFFICIAL_SUBURB` | `…/services/Valuations_Suburbs_for_2022_and_2025/FeatureServer/0` |
| Owner / title deed / transfers | erf or address | AfriGIS (§4) — the only national path |

---

## 2. Source reference — fields available

### City of Cape Town, Land Parcels (848 794 features, no key)

`SG26_CODE` `PRTY_NMBR` `SL_LAND_PRCL_KEY` `ADR_NO` `ADR_NO_SFX` `STR_NAME` `LU_STR_NAME_TYPE`
`OFC_SBRB_NAME` `ALT_NAME` `WARD_NAME` `SUB_CNCL_NMBR` `LU_LGL_STS_DSCR` `ZONING` `Shape__Area` `Shape__Length`

### Zoning (843 107 features)

`SG26_CODE` `INT_ZONE_CODE` `INT_ZONE_DESC` `INT_ZONE_VALUE` `LU_LGL_STS_KEY`
Example: `R1` → `Residential 1 : Conventional Housing`

### Valuation suburbs (659 rows)

`OFFICIAL_SUBURB` `NUM_RES_PROP` `MED_LAND_EXTENT_m2` `MED_TOT_BLD_AREA_m2` `GV2022_VAL` `GV2025_VAL`
Example: Kalk Bay — 250 properties, 453.5 m² median land, 273 m² median building, R6 050 000 → R8 250 000.

### Building plan approvals (247 845 records, 2014–2026)

`ERF_Number` `Suburb` `Case_ID` `Case_Type` `Case_Title` `District_Office__` `Service_Type__`
`Primary_Category__` `Plan_Category__` `Secondary_Category__` `Building_Work_Descri`
`Type_of_Work_Description` `Number_of_Units` `Area_of_New_Work` `Building_Work_Value`
`Submission_Date` `First_Amendment_Request_Date` `Latest_Amendment_Requested_On` `Amendment_Submission_Date`
`Approval_Date` `Commencement_Date` `Completion_Date` `OCC_Issued_Date` `RDP___LCH_Indicator`
`Ward_No` `Sub_council` `Financial_Year`

Dates are epoch milliseconds.

### Also in the portal

Sectional Title Scheme, Estates, Special Rating Areas, Street Address Numbers, 2D Building Footprints
(and Low Detail), CCT Buildings, Tree Canopies, Home Owners Associations (GV2009/2015/2018/2021),
Heritage Inventory, Local Area Overlay Zones, Urban Development Zones.

---

## 3. AfriGIS — the national layer (free trial account, OAuth2, 3 credentials)

Base: `https://afrigis.services/…` · docs: `https://developers.afrigis.co.za/oas3/<name>-api/`

### Property Search v3 — ownership and title

`GET /v3/ownership/{address|erf|landparcel|titledeed|scheme|farm|location}` and `GET /v3/person`

Response shape: `township` `registration_div` `erf` `portion` `scheme{name,number,year,unit}` `extent`
`lpicode` `cadastral_reference` `latest_transfer_details{purchased_price, purchased_date, registration_date,
title_deed, buyers[{name, id, marital_status, type, share_perc}]}`

### Property Transfer — full history per parcel

`GET /v3/transfers/landparcel?reference=…&purchased_date_from=…&registration_date_from=…`
→ `deeds_office`, `type`, `township`, `registration_div`, `erf`, `portion`, `scheme`, `extent`, `lpicode`,
`cadastral_reference`, `transfer_details[]`, `address`

### Property Analysis — market trend from 1990

`GET /property-analysis/v1/sales/annual/address?reference=…&property_level=…&property_class=…`
`GET /property-analysis/v1/bonds/annual/address?…` (same params)

Per `registration_year`: `median` `median_variance` `median_variance_perc` `percentile_25`
`percentile_25_variance` `percentile_25_variance_perc` `percentile_75` `percentile_75_variance`
`percentile_75_variance_perc` `stddev` `stddev_variance` `stddev_variance_perc` `avg` `avg_variance`
`avg_variance_perc` `lowest_sale` `highest_sale` `total_sales` `total_properties_sold`

Plus `address{place_id, seoid, sfid, formatted_address, confidence, location{lat,lng}, types}` and
`administrative_divisions{adminarea_l1..l3_name/_type, locality_name/_type, sublocality_name/_type, country}`

### Property Diagrams — SG diagrams

`GET /v1/data/by-coordinate` · `/v1/data/by-seoid` · `/v1/data/download-image` · `/download-thumbnail` · `/download-zip`
→ `documents[{documentNumber, pageNumber, id}]`, `zipFileDownloadId`

---

## 4. Report specification

| # | Section | Source | Notes |
|---|---|---|---|
| 1 | Property identification | roll + parcels | address, erf, SG26, valuation reference, registered description, legal status |
| 2 | Site | parcels + zoning | geodesic extent, zoning code and description, ward, sub-council, HOA/estate, special rating area |
| 3 | Improvements | footprints + plan approvals | roof area per building, height, storey estimate, approved work since 2014 with value |
| 4 | Municipal valuation | roll | GV2025 market value, rating category, effective date, dispute window |
| 5 | Comparable sales | roll Sales view (CT) / AfriGIS transfers (national) | filter, then price per m² — see §5 |
| 6 | Market trend | AfriGIS Property Analysis + suburb medians | annual median with 25th/75th band, volumes, GV2022→GV2025 suburb move |
| 7 | Ownership and title | AfriGIS Property Search | owner, title deed, purchase price and date — see §6 on personal information |
| 8 | Bond context | AfriGIS bonds annual | registration volumes as a demand signal |
| 9 | Site plan | parcel polygon + footprints, rendered by us | print-safe, no attribution constraint |
| 10 | Diagrams | AfriGIS Property Diagrams | SG diagram image |
| 11 | Imagery | Google Static Maps / Street View | **read §6 before designing the PDF** |
| 12 | Valuation opinion | derived | present a range, not a single number |

---

## 5. Comparable-sale hygiene (implement before anything else)

1. **Drop `Sale Price = 0` rows.** The roll's sales list includes non-arm's-length transfers; the verified
   Claremont set had them.
2. **Drop implausible prices** (below ~R100 000 for a suburban residential erf) — these are part-transfers,
   family transfers, correction deeds.
3. **Compute two ratios**: price ÷ erf extent and price ÷ dwelling extent. The second is the one that
   correlates; the first is the one clients ask about.
4. **Time-adjust.** Use the suburb GV2022→GV2025 move, or the AfriGIS annual median series, to index older
   sales to today. Kalk Bay moved +36% over three years — an unindexed 2023 comparable is simply wrong.
5. **Filter on similarity, not just distance.** The roll's "general area" is the City's own neighbourhood
   definition, not a radius you control, so tighten it yourself on dwelling extent (±25%) and erf extent.
6. **Never present fewer than 3 comparables**, and show the excluded count so the number looks considered.

---

## 6. Compliance rules to encode (not decisions to relitigate in code review)

**Google Street View imagery must not appear in the printed or PDF report.** Google's Geo Guidelines bar
Street View from print use, and unlike the Maps/Earth print allowance it carries no small-volume exception.
Show it on screen in the app instead. Practical design: the PDF gets the satellite view and our own site plan;
the app screen gets the Street View pane.

**Google Maps / Earth satellite imagery may go in the printed report** as a business document under 5 000
copies, with the Google attribution visible next to the image, uncropped and unaltered. Do not relocate the
attribution into a footer or credits page.

**Do not store Google imagery.** Cache the lat/lng (30 days is permitted) and the Street View `pano_id`, and
re-request images at render time. No pre-fetching, no bulk download, no image bytes in our database.

**Owner data is personal information under POPIA.** Names, ID numbers and marital status come back from the
deeds APIs. Constraints to build in: a report given to the owner of that property is the straightforward case;
do not print third parties' owner names in the comparables table; keep an audit log of who pulled which
property's owner data; set a retention period; and confirm the display right in the data provider's contract
before the field reaches a screen.

**Municipal pages are under the City's terms of use.** Read them before pulling at volume, rate-limit politely,
cache the parsed result on our side so one report is one fetch, and identify the client honestly.

**Deeds data carries redistribution limits.** Every provider contract restricts caching and re-display. Check
the specific terms before storing transfer history as our own dataset.

---

## 7. Provider abstraction

Write adapters into one internal model. Providers will be swapped on price once there are users, and every one
of them returns a different shape.

```ts
interface PropertyRef {
  erf: string;                     // "53927"
  sg26?: string;                   // "C0160007000539270000000000"
  valuationRef?: string;           // "CCT010812600000"
  township: string;                // "CAPE TOWN"
  suburb: string;                  // "CLAREMONT"
  municipality: 'coct' | 'joburg' | 'tshwane' | 'ethekwini' | 'other';
}

interface PropertyRecord {
  ref: PropertyRef;
  address: { formatted: string; streetNo: number; streetName: string; streetType?: string };
  location: { lat: number; lng: number };
  site: {
    extentM2: number;              // geodesic, never Shape__Area
    extentSource: 'deed' | 'roll' | 'geodesic';
    zoningCode?: string;
    zoningDescription?: string;
    ward?: string;
    legalStatus?: string;          // "Registered"
    boundary?: GeoJSON.Polygon;
  };
  improvements: {
    dwellingExtentM2?: number;     // roll's own figure — prefer this
    buildings: { roofM2: number; heightM?: number; capturedYYYYMM?: number; outline?: GeoJSON.Polygon }[];
    approvedWork: { date: string; description: string; areaM2?: number; valueZar?: number }[];
  };
  municipalValuation?: {
    valueZar: number; asAt: string; category: string;
    rollVersion: string; effectiveFrom: string; disputeExpiry?: string;
  };
  ownership?: {                    // gated behind the POPIA checks in §6
    owners: { name: string; sharePerc?: number; maritalStatus?: string }[];
    titleDeed?: string; purchasePriceZar?: number; purchaseDate?: string; registrationDate?: string;
  };
  transfers?: { titleDeed: string; priceZar: number; purchaseDate: string; registrationDate: string }[];
  provenance: { field: string; source: string; fetchedAt: string }[];   // cite every number in the report
}

interface Comparable {
  address: string; erf: string;
  erfExtentM2: number; dwellingExtentM2: number;
  saleDate: string; salePriceZar: number;
  pricePerErfM2: number; pricePerDwellingM2: number;
  indexedPriceZar?: number;        // time-adjusted to report date
  excluded?: 'zero-price' | 'implausible' | 'dissimilar';
}

interface PropertyDataProvider {
  resolve(input: { address?: string; lat?: number; lng?: number; erf?: string }): Promise<PropertyRef[]>;
  fetchRecord(ref: PropertyRef): Promise<PropertyRecord>;
  fetchComparables(ref: PropertyRef, opts?: { radiusM?: number; sinceYear?: number }): Promise<Comparable[]>;
  fetchTrend(ref: PropertyRef): Promise<{ year: number; median: number; p25: number; p75: number; volume: number }[]>;
}
```

`provenance` is worth the effort: a valuation report is only as good as its citations, and it is what lets an
agent defend a number in front of a seller.

---

## 8. Credentials to ask the human for

| Env var | What | How to get it |
|---|---|---|
| `AFRIGIS_KEY` / `AFRIGIS_SECRET` / `AFRIGIS_CLIENT` | OAuth2, three credentials | free trial account by email via the AfriGIS developer portal |
| `GOOGLE_MAPS_KEY` | Static Maps, Street View Static, Geocoding | Google Cloud; 10 000 free calls per SKU per month, then $2.00 / $7.00 / $5.00 per 1 000 |

Cape Town's ArcGIS and roll endpoints need **no credentials**. Restrict the Google key by API and by referrer
or IP before it ships.

Geocoding alternatives if the Google spend matters: AfriGIS geocode API (on the same trial, better SA address
matching), or skip geocoding entirely in Cape Town using the attribute query in §1a.

---

## 9. Golden test fixture

Verified 26 September 2026. Use these as assertions so a provider change or a City schema change fails loudly.

```
input:  "17 Pine Road, Claremont, Cape Town"
expect: erf                    53927
        sg26                   C0160007000539270000000000
        valuationRef           CCT010812600000
        centroid               -33.989740, 18.470992   (±0.0002)
        zoning                 General Residential 2
        ward                   59
        extentM2 (roll)        1085
        extentM2 (geodesic)    1079  (±10)
        dwellingExtentM2       300
        municipalValue         7100000  @ 2025-07-01, RESIDENTIAL, GV2025
        mainBuildingRoofM2     345   (±5)
        mainBuildingHeightM    7.6
        footprintCapture       201312
        comparablesAvailable   > 2000 raw, before filtering
anti-assertion:
        Shape__Area            1569  ← must NOT be used as extent
```

---

## 10. Build order

1. CoCT adapter: address normalisation → parcel lookup → roll scrape → sales parse. Golden test from §9.
2. Comparable filtering and indexing (§5). This is where the product's credibility lives.
3. SVG site plan from parcel polygon + footprints. Print-safe, no licence entanglement.
4. Report renderer with `provenance` citations; satellite image with attribution; Street View on screen only.
5. AfriGIS adapter for ownership, transfers and trend — this is what takes the app national.
6. Only then consider a paid AVM. Until then present a range derived from the municipal value, filtered
   comparables per m², and the suburb trend.
