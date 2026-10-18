using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// ============================ A CLASSE MAIS RARA GARANTIDA PELO ADMIN (`--classerarateste`, 2026-10-16) ============================
/// O dono: *"adicione um verb pra adm q faz com q eu possa escolher uma conta ja feita no server, e o proximo personagem criado por
/// essa conta vai garantidamente ser da classe mais rara dessa raça, no modo aleatorio a raça vem aleatoria mas sempre da classe mais
/// rara, e no modo q escolhe a raça a classe tb vem a mais rara"*.
///
/// SERVIDOR PURO, NO BOOT. Tudo pelas funcoes de producao que o `CreateChar` e o verb chamam: `Birth.ClasseMaisRara`, o
/// `GastarAClasseGarantida` + `Nascer` (a mesma dupla, na mesma ordem, da criacao), o `PrepararVagas` do modo aleatorio e o
/// `AdminClasseRara` sobre uma conta GRAVADA (o verb acha a conta pelo `_store`, como acha pra promover).
///
/// AS FAMILIAS: 1. a tabela (a mais rara de cada raca, o empate, as que nao tem) · 2. o nascimento com a garantia (a classe, a
/// linhagem que E a classe, o gasto, e a raca sem classe rara que GUARDA) · 3. o sorteio de raca no modo aleatorio (so racas com
/// classe rara -- e o controle sem a garantia) · 4. o verb (alterna, grava, refaz os sorteios das vagas vazias) · 5. o CONTROLE
/// `ClasseRaraIgnoradaDeTeste`: com ele, a classe volta ao sorteio e a garantia nao se gasta.
/// ===================================================================================================================================
/// </summary>
public partial class GameServer
{
	private void RodarBancadaDaClasseRara()
	{
		GD.Print("\n[classerara] ================ A CLASSE MAIS RARA GARANTIDA PELO ADMIN (dono, 2026-10-16) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[classerara]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[classerara]   FALHA {nome}   [{detalhe}]"); }
		}
		if (_racas == null || _store == null) { Checa("o catalogo de racas e o armazem de contas existem", false); return; }

		var rng = new Random(7);
		Jandirus.Core.Races.ModoDeRacas modoAntes = ModoDeRacas;
		const string Conta = "bancada_classerara";

		try
		{
			// ------------------------------------------------------------------ 1. a tabela
			(string Raca, string Linhagem, string[] Esperadas)[] casos =
			[
				("Human", "", ["Ancient Hermit"]), ("Icer", "", ["Mutant Frost Demon"]), ("Kai", "", ["Golden Apple"]),
				("Majin", "", ["Corrupted Majin"]), ("Gray", "", ["Hermano"]), ("Heran", "", ["Omega"]), ("BioAndroid", "", ["Majin-Type"]),
				("Saiyan", "Saiyan", ["Legendary"]), ("Saiyan", "Primal Saiyan", ["Legendary Primal Saiyan"]),
				("Namekian", "Warrior clan", ["Warrior clan"]), ("Namekian", "Dragon clan", ["Warrior clan", "Demon clan"]),
				("Demigod", "", []), ("Alien", "", []), ("Halfbreed", "", []), ("Android", "", []),
			];
			foreach ((string raca, string linhagem, string[] esperadas) in casos)
			{
				string rara = Birth.ClasseMaisRara(_racas, raca, linhagem, rng);
				Checa($"1. a mais rara de {raca}{(linhagem.Length > 0 ? $"/{linhagem}" : "")}: "
					+ (esperadas.Length == 0 ? "NENHUMA (sem sorteio, uma classe so, ou todas iguais)" : string.Join(" ou ", esperadas)),
					esperadas.Length == 0 ? rara.Length == 0 : esperadas.Contains(rara), $"'{rara}'");
			}

			// ------------------------------------------------------------------ 2. o nascimento com a garantia
			foreach ((string raca, string linhagem, string[] esperadas) in casos.Where(c => c.Esperadas.Length > 0))
			{
				var acc = new AccountSave { Conta = Conta, ProximaClasseRara = true };
				var ficha = new CharacterDraft { Name = "Rara", Race = raca, ChosenClass = linhagem, Age = 20 };
				string classe = GastarAClasseGarantida(acc, ficha);
				Fighter f = Nascer(ficha, "Rara", classe);
				Checa($"2. {raca}{(linhagem.Length > 0 ? $"/{linhagem}" : "")} com a garantia NASCE {string.Join(" ou ", esperadas)} -- e a garantia se gasta",
					esperadas.Contains(f.Class) && f.Class == classe && !acc.ProximaClasseRara, $"classe '{f.Class}', garantia {acc.ProximaClasseRara}");
				if (raca == "Namekian")
					Checa("...e o Namekuseijin leva a linhagem IGUAL a classe (o cla escolhido E a classe)", ficha.ChosenClass == f.Class, ficha.ChosenClass);
			}
			{
				var acc = new AccountSave { Conta = Conta, ProximaClasseRara = true };
				var ficha = new CharacterDraft { Name = "Rara", Race = "Alien", Age = 20 };
				string classe = GastarAClasseGarantida(acc, ficha);
				Checa("...e uma raca SEM classe rara (o Alien) nasce normal e GUARDA a garantia pro proximo", classe.Length == 0 && acc.ProximaClasseRara, $"'{classe}'");
			}

			// ------------------------------------------------------------------ 3. o sorteio de raca no modo aleatorio
			ModoDeRacas = Jandirus.Core.Races.ModoDeRacas.Aleatorio;
			List<string> Sortear(bool garantia)
			{
				var acc = new AccountSave { Conta = Conta + (garantia ? "_g" : "_c"), ProximaClasseRara = garantia };
				var racas = new List<string>();
				for (int volta = 0; volta < 120; volta++)
				{
					for (int i = 0; i < AccountStore.Slots; i++) acc.Sorteios[i] = null;
					PrepararVagas(acc);
					foreach (RacaSorteada? s in acc.Sorteios) if (s != null) racas.Add(s.Raca);
				}
				return racas;
			}
			List<string> comGarantia = Sortear(true), semGarantia = Sortear(false);
			Checa("3. modo ALEATORIO com a garantia: a raca sorteada sai SO entre as que tem classe rara",
				comGarantia.Count > 300 && comGarantia.All(r => Birth.TemClasseRara(_racas, r)),
				$"{comGarantia.Count} sorteios, {comGarantia.Distinct().Count()} racas: {string.Join(", ", comGarantia.Distinct())}");
			Checa("...e o CONTROLE sem a garantia sorteia tambem as que nao tem (Alien, Android...)",
				semGarantia.Any(r => !Birth.TemClasseRara(_racas, r)), $"{semGarantia.Count(r => !Birth.TemClasseRara(_racas, r))} sem classe rara");

			// ------------------------------------------------------------------ 4. o verb, sobre uma conta gravada
			var gravada = new AccountSave { Conta = Conta };
			gravada.Sorteios[0] = new RacaSorteada { Raca = "Alien" };
			_store.Gravar(gravada);
			var adm = new ServerPlayer { Id = 0x7160_0000, Name = "bancada: o admin", Conta = "bancada_classerara_adm" };
			AdminClasseRara(adm, Conta);
			AccountSave? lida = _store.Achar(Conta);
			Checa("4. o verb CONCEDE e grava na conta -- e refaz o sorteio pendente da vaga vazia",
				lida is { ProximaClasseRara: true } && lida.Sorteios[0] is null, $"garantia {lida?.ProximaClasseRara}, vaga 1 {lida?.Sorteios[0]?.Raca ?? "sem sorteio"}");
			AdminClasseRara(adm, Conta);
			lida = _store.Achar(Conta);
			Checa("...e o mesmo verb CANCELA (alterna)", lida is { ProximaClasseRara: false }, $"garantia {lida?.ProximaClasseRara}");

			// ------------------------------------------------------------------ 5. o controle
			ClasseRaraIgnoradaDeTeste = true;
			try
			{
				var acc = new AccountSave { Conta = Conta, ProximaClasseRara = true };
				var classes = new List<string>();
				for (int i = 0; i < 40; i++)
				{
					var ficha = new CharacterDraft { Name = "Rara", Race = "Human", Age = 20 };
					classes.Add(Nascer(ficha, "Rara", GastarAClasseGarantida(acc, ficha)).Class);
				}
				Checa("5. CONTROLE (`ClasseRaraIgnoradaDeTeste`): sem a garantia aplicada, 40 Humanos saem do SORTEIO (nao todos Ancient Hermit) e ela nao se gasta",
					classes.Any(c => c != "Ancient Hermit") && acc.ProximaClasseRara, string.Join(", ", classes.Distinct()));
			}
			finally { ClasseRaraIgnoradaDeTeste = false; }
		}
		finally
		{
			ModoDeRacas = modoAntes;
			ClasseRaraIgnoradaDeTeste = false;
			GD.Print($"[classerara] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}
}
