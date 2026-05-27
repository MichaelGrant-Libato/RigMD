import os
from google import genai
from google.genai import types

# Initialize client using modern google-genai library
client = genai.Client(api_key=os.environ.get("GEMINI_API_KEY"))

def generate_triage_explanation(user_data: dict, system_specs: dict) -> dict:
    """
    Dynamically analyzes incoming symptom parameters using Gemini, 
    instructing it to translate dense hardware analytics into elegant layman terms.
    """
    
    system_instruction = """
    You are the core intelligence of RigMD, an elite computer diagnostic system.
    Your objective is to diagnose system anomalies for NON-TECHNICAL users.
    
    CRITICAL INSTRUCTIONS FOR RESPONSE TONE:
    1. Do not give cold, rigid, or overly dense engineering paragraphs.
    2. Explain the root hardware cause using clear, real-world analogies (e.g., comparing an overheated CPU throttling to an athlete slowing down to catch their breath so they do not collapse).
    3. Be technically accurate under the hood, but translate the impact to match the user's daily activity.
    4. Speak like an expert tech peer sitting right next to them, not a textbook.
    5. Structural Rule: Your text output must flow naturally in continuous paragraphs. Never include numbered lists, bullet points, or markdown list symbols (*, -, 1., 2.) anywhere in your reasoning.
    """

    user_context_prompt = f"""
    Analyze this incident report:
    - User Observed Anomaly Type: {user_data.get('symptom_type')}
    - Perceived Severity: {user_data.get('severity')}
    - Occurrence Frequency: {user_data.get('frequency')}
    - Active Activity Trigger: {user_data.get('affected_activity')}
    - Warning Signs Noted: {user_data.get('warning_signs')}
    - User Notes: {user_data.get('system_state')}
    
    Detected Host Hardware Specs (Use this as your environment context baseline):
    - Processor Context: {system_specs.get('cpu', 'Unknown CPU')}
    - Memory Bounds: {system_specs.get('ram', 'Unknown Memory')}
    - Operating System Environment: {system_specs.get('os_version', 'Unknown Windows Build')}
    """

    # Call Gemini API dynamically
    response = client.models.generate_content(
        model='gemini-2.5-flash',
        contents=user_context_prompt,
        config=types.GenerateContentConfig(
            system_instruction=system_instruction,
            temperature=0.7,
            # Ensuring structured JSON matching your frontend expected interfaces
            response_mime_type="application/json",
            response_schema=types.Schema(
                type=types.Type.OBJECT,
                properties={
                    "diagnosed_category": types.Schema(type=types.Type.STRING),
                    "action_category": types.Schema(type=types.Type.STRING),
                    "confidence_label": types.Schema(type=types.Type.STRING),
                    "ai_explanation": types.Schema(type=types.Type.STRING),
                    "recommended_next_step": types.Schema(type=types.Type.STRING),
                },
                required=["diagnosed_category", "action_category", "confidence_label", "ai_explanation", "recommended_next_step"]
            )
        )
    )
    
    import json
    return json.loads(response.text)