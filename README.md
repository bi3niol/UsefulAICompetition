# Business Overview

DevOps Impact Analyzer to rozwiązanie stworzone dla zespołów, które rozwijają systemy przez lata i potrzebują szybkiego dostępu do kontekstu zmian przed rozpoczęciem implementacji. 🚀

---

## 🌍 Z czym mierzą się zespoły na co dzień

W projektach prowadzonych przez wiele lat liczba zaimplementowanych funkcjonalności rośnie tak szybko, że żaden zespół nie utrzymuje pełnego obrazu systemu w pamięci. 🧠

Rotacja ludzi, zmiany w analizie biznesowej i pęczniejąca dokumentacja powodują, że wiedza o szczegółach istniejących mechanizmów jest rozproszona pomiędzy DevOps, kodem i WIKI. 📚

Efekt? Nowe wymagania, błędy i pomysły są analizowane bez pełnego kontekstu. ⚠️

Najczęstsze konsekwencje:

- ❌ **Sprzeczne wymagania** – nowe wymaganie wchodzi w konflikt z istniejącymi mechanizmami lub wpływa na nie w nieprzewidziany sposób.
- 👥 **Duplikaty wymagań** – kilka osób równolegle pracuje nad analogicznym rozwiązaniem, tworząc podwójne tickety i różne implementacje tego samego problemu.
- 🔁 **Powracające błędy** – przy nowych zgłoszeniach nie wykorzystuje się wiedzy o podobnych incydentach z przeszłości.

### 🔗 Wspólny mianownik

We wszystkich tych przypadkach brakuje jednego elementu: **szybkiego, na żądanie spojrzenia na zależności pomiędzy istniejącymi elementami DevOps i dokumentacją**.

---

## ✅ Gdzie pomaga DevOps Impact Analyzer

**DevOps Impact Analyzer** na żądanie buduje raport zależności dla wybranego itemu Azure DevOps (Feature, Bug, User Story, Task i inne). 🎯

Narzędzie analizuje równocześnie:

- 🧩 istniejące elementy DevOps (powiązania, historia, opisy),
- 📘 dokumentację WIKI,
- 🏗️ kontekst architektoniczny i wcześniejsze decyzje.

Następnie wskazuje:

- 🎯 na co dane wymaganie może wpłynąć,
- ⚔️ z czym może być sprzeczne,
- 🕸️ jakie elementy są powiązane i wymagają uwagi.

**Jeden raport – trzy scenariusze zastosowania:**

1. 🆕 weryfikacja nowego wymagania,
2. 🪞 wykrywanie duplikatów,
3. 🐞 wsparcie analizy błędów na podstawie wcześniejszych doświadczeń.

---

## 🤖 Jak to działa w praktyce

DevOps Impact Analyzer jest aplikacją .NET 10 Azure Functions wykorzystującą pipeline 4 agentów AI:

- 🔎 **Researcher** – prowadzi szerokie badanie kontekstu w DevOps i WIKI,
- ✍️ **Writer** – buduje uporządkowany raport markdown,
- 🧪 **Editor** – sprawdza kompletność i jakość merytoryczną,
- 📤 ~~**Sender** – przygotowuje wynik do publikacji w Azure DevOps.~~

🧩 Gotowy raport jest prezentowany **bezpośrednio w Azure DevOps, w kontekście konkretnego work itemu**, za pomocą dedykowanej wtyczki do przeglądarki (Chrome/Edge). Dzięki temu użytkownik nie przełącza się między narzędziami i od razu widzi pełny kontekst analizy.

### 🎬 Demo video

[![▶ Zobacz krótkie demo działania](https://img.youtube.com/vi/QQDeOF03B40/hqdefault.jpg)](https://www.youtube.com/watch?v=QQDeOF03B40)

W zależności od typu work itemu system generuje:

- 📄 **raport impact analysis** (dla wymagań, User Stories, Feature, Task),
- 🩺 **raport diagnozy błędu** (dla Bugów, z naciskiem na podobne incydenty i możliwe przyczyny źródłowe),
- 🌐 **raport w języku analizowanego itemu** (język treści raportu jest automatycznie dopasowany do języka work itemu).

---

## 💼 Co zyskuje biznes

- ⚡ szybszą i trafniejszą analizę zmian,
- 🛡️ mniejsze ryzyko kosztownych konfliktów i regresji,
- 🧹 redukcję duplikowanej pracy zespołów,
- 🧠 lepsze wykorzystanie wiedzy historycznej organizacji,
- 📈 bardziej przewidywalne planowanie i realizację.

---

## 🧭 Najkrócej

DevOps Impact Analyzer zamienia rozproszoną wiedzę projektową w **konkretny, gotowy do użycia raport decyzyjny**. Dzięki temu zespoły podejmują lepsze decyzje wcześniej — zanim koszt zmian zacznie rosnąć. 💡

---

## 🔗 Linki

- 📘 [DevOps Impact Analyzer App](./BSolution.Netwise.UsefulAI/BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/README.md)
  - 🗺️ [Solution diagrams](./BSolution.Netwise.UsefulAI/BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App/docs/solution-diagrams.md)
- 🧩 [DevOps Impact Analyzer Extension](./BSolution.Netwise.UsefulAI/BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.Extension/README.md)
